using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Host;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>Settings → Security → Image security check, kept in <c>image-security.json</c>; blocking until changed.</summary>
public sealed class ImageSecuritySettingsStore(IOptions<WslcOptions> options)
    : SavedSettings<ImageSecuritySettings>(Path.Combine(options.Value.DataDirectory, "image-security.json"), new ImageSecuritySettings());

/// <summary>A known vulnerability of one Python library in an image.</summary>
/// <param name="Severity"><c>low</c>, <c>moderate</c>, <c>high</c>, <c>critical</c>, or <c>unknown</c> when the advisory gives none.</param>
/// <param name="FixedIn">The first version that fixes it; empty when none does yet.</param>
public sealed record ImageVulnerability(string Package, string Version, string Advisory, string Cve, string Severity, string FixedIn, string Url)
{
    /// <summary>Moderate or worse: what stops a build under <see cref="ImageSecurityPolicy.Block"/>.</summary>
    public bool Blocks => Severity is "moderate" or "high" or "critical";

    public override string ToString() =>
        $"{Package} {Version}, {Severity}: {Advisory}{(Cve.Length > 0 ? $" ({Cve})" : "")}, fixed in {(FixedIn.Length > 0 ? FixedIn : "no version yet")}";
}

/// <summary>
/// The check every image the agent builds goes through before a container is
/// made from it (<see cref="ImageSecuritySettings"/>): the image's Python
/// libraries, read in the image itself with <c>pip list</c>, so the versions
/// are the ones it carries whatever its requirements pin, asked of OSV.dev,
/// each library once. An image with no Python has nothing to check. The
/// check is a safety net, not the build: when OSV.dev cannot be reached, or
/// the image cannot be read, it says so in the log and the build goes on.
/// </summary>
public sealed class ImageSecurity(IWslcRunner wslc, HttpClient http, ImageSecuritySettingsStore settings, ILogger<ImageSecurity> logger)
{
    /// <summary>A large image's first start, and pip reading its site-packages.</summary>
    private static readonly TimeSpan ListTimeout = TimeSpan.FromMinutes(3);

    /// <summary>An advisory changes seldom: each is read once a day.</summary>
    private static readonly TimeSpan AdvisoryKept = TimeSpan.FromDays(1);

    private readonly ConcurrentDictionary<string, (JsonNode? Advisory, DateTimeOffset Read)> _advisories = new();

    /// <summary>
    /// Checks the images just built, writing what it does and finds through
    /// <paramref name="note"/> (a level and a text, as a job's log lines go).
    /// Under <see cref="ImageSecurityPolicy.Block"/>, an image with a
    /// vulnerability moderate or worse throws <see cref="InvalidOperationException"/>,
    /// naming each one: the job fails before any container is made from it.
    /// </summary>
    public async Task GuardAsync(IReadOnlyList<string> images, Action<string, string> note, CancellationToken cancellationToken)
    {
        var policy = settings.Get().Policy;
        if (policy == ImageSecurityPolicy.Off)
        {
            note("DEBUG", "Image security check is off (Settings → Security): the images were not checked.");
            return;
        }

        var stopping = new List<string>();
        foreach (var image in images)
        {
            note("INFO", $"Checking {image}'s Python libraries for known vulnerabilities");
            var found = await CheckAsync(image, note, cancellationToken);
            foreach (var vulnerability in found)
            {
                note("WARNING", $"{image}: {vulnerability}");
            }

            stopping.AddRange(found.Where(v => v.Blocks).Select(v => $"{image}: {v}"));
        }

        if (stopping.Count == 0)
        {
            return;
        }

        if (policy == ImageSecurityPolicy.Block)
        {
            throw new InvalidOperationException(
                $"{stopping.Count} Python librar{(stopping.Count == 1 ? "y" : "ies")} with a vulnerability moderate or worse, so no container was made: "
                + string.Join("; ", stopping)
                + ". Raise them to a version that fixes it and build again; Settings → Security → Image security check set to Warn builds anyway.");
        }

        note("WARNING", $"{stopping.Count} vulnerabilit{(stopping.Count == 1 ? "y" : "ies")} moderate or worse; built anyway, as Settings → Security → Image security check is set to Warn.");
    }

    /// <summary>The image's vulnerabilities; none when it has no Python, or when it could not be checked (said through <paramref name="note"/>).</summary>
    private async Task<List<ImageVulnerability>> CheckAsync(string image, Action<string, string> note, CancellationToken cancellationToken)
    {
        List<(string Name, string Version)>? packages;
        try
        {
            packages = await PythonPackagesAsync(image, cancellationToken);
        }
        catch (Exception ex) when (ex is TimeoutException or WslcNotFoundException)
        {
            note("WARNING", $"{image} could not be read ({ex.Message}): not checked.");
            return [];
        }

        if (packages is null)
        {
            note("INFO", $"{image} has no Python with pip: nothing to check.");
            return [];
        }

        try
        {
            var found = await OsvAsync(packages, cancellationToken);
            if (found.Count == 0)
            {
                note("INFO", $"{image}: {packages.Count} Python libraries, no known vulnerability.");
            }

            return found;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("image security check of {Image}: OSV.dev did not answer: {Message}", image, ex.Message);
            note("WARNING", $"{image}: OSV.dev could not be reached ({ex.Message}): not checked.");
            return [];
        }
    }

    /// <summary>
    /// <c>pip list</c> run in the image itself, under a container of its own
    /// that is removed after (no <c>--rm</c>: wslc before 3.0.4 has none);
    /// null when the image has no Python with pip.
    /// </summary>
    private async Task<List<(string Name, string Version)>?> PythonPackagesAsync(string image, CancellationToken cancellationToken)
    {
        foreach (var python in new[] { "python", "python3" })
        {
            var name = $"wslc-ai-agent-check-{Guid.NewGuid():N}"[..32];
            try
            {
                var result = await wslc.RunAsync(
                    ["container", "run", "--name", name, "--pull", "never", "--entrypoint", python, image, "-m", "pip", "list", "--format", "json", "--disable-pip-version-check"],
                    ListTimeout, cancellationToken);
                return Packages(result.Stdout);
            }
            catch (WslcException)
            {
                // No such program in the image, or no pip: the next name, then nothing.
            }
            finally
            {
                await RemoveAsync(name);
            }
        }

        return null;
    }

    private async Task RemoveAsync(string container)
    {
        try
        {
            await wslc.RunAsync(["container", "rm", "--force", container]);
        }
        catch (Exception ex) when (ex is WslcException or TimeoutException or WslcNotFoundException)
        {
            // Never created (the image had no such program), or already gone.
        }
    }

    /// <summary><c>pip list --format json</c>: <c>[{"name": "fastapi", "version": "0.115.0"}, …]</c>.</summary>
    private static List<(string Name, string Version)>? Packages(string output)
    {
        var start = output.IndexOf('[');
        if (start < 0)
        {
            return null;
        }

        return [.. (JsonNode.Parse(output[start..])?.AsArray() ?? [])
            .Select(p => ((string?)p?["name"] ?? "", (string?)p?["version"] ?? ""))
            .Where(p => p.Item1.Length > 0 && p.Item2.Length > 0)];
    }

    /// <summary>OSV.dev's known vulnerabilities of these PyPI libraries, one batch question, each advisory read once.</summary>
    private async Task<List<ImageVulnerability>> OsvAsync(List<(string Name, string Version)> packages, CancellationToken cancellationToken)
    {
        var found = new List<ImageVulnerability>();
        foreach (var batch in packages.Chunk(500))
        {
            using var response = await http.PostAsJsonAsync("https://api.osv.dev/v1/querybatch", new
            {
                queries = batch.Select(p => new { package = new { name = p.Name, ecosystem = "PyPI" }, version = p.Version }),
            }, cancellationToken);
            response.EnsureSuccessStatusCode();
            var results = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?["results"]?.AsArray() ?? [];
            for (var i = 0; i < results.Count && i < batch.Length; i++)
            {
                var ids = (results[i]?["vulns"]?.AsArray() ?? []).Select(v => (string?)v?["id"]).OfType<string>().ToList();
                var advisories = new List<(string Id, JsonNode? Advisory)>();
                foreach (var id in ids)
                {
                    advisories.Add((id, await AdvisoryAsync(id, cancellationToken)));
                }

                // One vulnerability is often both a PYSEC and a GHSA advisory, each naming the other: the GHSA one is kept, it carries the severity.
                foreach (var (id, advisory) in advisories.Where(a => a.Id.StartsWith("GHSA-", StringComparison.Ordinal)
                    || !Aliases(a.Advisory).Any(alias => ids.Contains(alias))))
                {
                    found.Add(Read(id, advisory, batch[i]));
                }
            }
        }

        return found;
    }

    private async Task<JsonNode?> AdvisoryAsync(string id, CancellationToken cancellationToken)
    {
        if (_advisories.TryGetValue(id, out var kept) && DateTimeOffset.UtcNow - kept.Read < AdvisoryKept)
        {
            return kept.Advisory;
        }

        var advisory = JsonNode.Parse(await http.GetStringAsync($"https://api.osv.dev/v1/vulns/{id}", cancellationToken));
        _advisories[id] = (advisory, DateTimeOffset.UtcNow);
        return advisory;
    }

    private static IEnumerable<string> Aliases(JsonNode? advisory) =>
        (advisory?["aliases"]?.AsArray() ?? []).Select(a => (string?)a).OfType<string>();

    private static ImageVulnerability Read(string id, JsonNode? advisory, (string Name, string Version) package)
    {
        var aliases = Aliases(advisory).ToList();
        var fixedIn = (advisory?["affected"]?.AsArray() ?? [])
            .Where(a => Normalized((string?)a?["package"]?["name"]) == Normalized(package.Name))
            .SelectMany(a => a?["ranges"]?.AsArray() ?? [])
            .SelectMany(r => r?["events"]?.AsArray() ?? [])
            .Select(e => (string?)e?["fixed"]).OfType<string>()
            .FirstOrDefault() ?? "";
        var severity = ((string?)advisory?["database_specific"]?["severity"] ?? "").Trim().ToLowerInvariant() switch
        {
            "medium" => "moderate",
            var known when known is "low" or "moderate" or "high" or "critical" => known,
            _ => "unknown",
        };
        return new ImageVulnerability(package.Name, package.Version, id, aliases.FirstOrDefault(a => a.StartsWith("CVE-", StringComparison.Ordinal)) ?? "",
            severity, fixedIn, $"https://osv.dev/vulnerability/{id}");
    }

    /// <summary>PyPI's names compare as PEP 503 says: case and <c>_</c>, <c>-</c>, <c>.</c> alike.</summary>
    private static string Normalized(string? name) => (name ?? "").ToLowerInvariant().Replace('_', '-').Replace('.', '-');
}
