using System.Collections.Concurrent;
using System.Formats.Tar;
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
/// <param name="Severity"><c>low</c>, <c>moderate</c>, <c>high</c>, <c>critical</c>, or <c>unknown</c> when neither the advisory, its GHSA alias nor its CVSS vector gives one.</param>
/// <param name="FixedIn">The first version that fixes it; empty when none does yet.</param>
public sealed record ImageVulnerability(string Package, string Version, string Advisory, string Cve, string Severity, string FixedIn, string Url)
{
    /// <summary>Moderate or worse: what stops a build under <see cref="ImageSecurityPolicy.Block"/>.</summary>
    public bool Blocks => Severity is "moderate" or "high" or "critical";

    public override string ToString() =>
        $"{Package} {Version}, {Severity}: {Advisory}{(Cve.Length > 0 ? $" ({Cve})" : "")}, {(FixedIn.Length > 0 ? $"fixed in {FixedIn}" : "no version fixes it yet")}";
}

/// <summary>
/// The agent makes no container from the image and starts none: built on this
/// machine, a vulnerability moderate or worse, Settings → Security set to
/// Block. <see cref="Said"/> is what the container is marked with, a line each;
/// <see cref="Worst"/> the worst severity among them; the container not
/// started, when it was one that exists (empty for a new one).
/// </summary>
public sealed class ImageRefusedException(string message, IReadOnlyList<string> said, string worst, string containerId = "", string containerName = "")
    : InvalidOperationException(message)
{
    public IReadOnlyList<string> Said => said;

    public string Worst => worst;

    public string ContainerId => containerId;

    public string ContainerName => containerName;
}

/// <summary>
/// The check every image the agent builds goes through before a container is
/// made from it (<see cref="ImageSecuritySettings"/>): the image's Python
/// libraries, read from the image's own files and never by running it, so the versions
/// are the ones it carries whatever its requirements pin, asked of OSV.dev,
/// each library once. An image with no Python has nothing to check. The
/// check is a safety net, not the build: when OSV.dev cannot be reached, or
/// the image cannot be read, it says so in the log and the build goes on.
/// It is made again before every new container (<see cref="GuardLaunchAsync"/>)
/// and every start of one (<see cref="GuardStartAsync"/>), since an advisory
/// found after the build is no less there.
/// <paramref name="keptIn"/>: the folder where each image's libraries are kept
/// once read, by its id, so a restart of the agent does not read a large image
/// again; none, and they are kept only while it runs.
/// </summary>
public sealed class ImageSecurity(IWslcRunner wslc, HttpClient http, ImageSecuritySettingsStore settings, ILogger<ImageSecurity> logger, string? keptIn = null) : WslcAgent.Mcp.IImageSecurity
{
    /// <summary>A large image's container made and its files exported, a gigabyte and more.</summary>
    private static readonly TimeSpan ListTimeout = TimeSpan.FromMinutes(5);

    /// <summary>An advisory changes seldom: each is read once a day.</summary>
    private static readonly TimeSpan AdvisoryKept = TimeSpan.FromDays(1);

    /// <summary>How many advisories are asked of OSV.dev at once.</summary>
    private const int AdvisoriesAtOnce = 8;

    /// <summary>The name of the throwaway container, never started, whose files are read: one of the agent's helpers, never listed nor checked itself.</summary>
    internal const string CheckPrefix = "wslc-ai-agent-check-";

    private const string Unknown = "unknown";

    private static readonly string[] SeverityOrder = [Unknown, "low", "moderate", "high", "critical"];

    private readonly ConcurrentDictionary<string, (JsonNode? Advisory, DateTimeOffset Read)> _advisories = new();

    /// <summary>
    /// The Python libraries of each image read so far, by the image's id: an
    /// image never changes under its id, so a second container made from it
    /// costs no second reading of its files, nor does a restart of the agent
    /// (<c>keptIn</c>); null for an image with no Python libraries. What the
    /// libraries are vulnerable to is asked anew each time.
    /// </summary>
    private readonly ConcurrentDictionary<string, List<(string Name, string Version)>?> _packages = new(StringComparer.Ordinal);

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

        var stopping = new List<(string Image, ImageVulnerability Vulnerability)>();
        foreach (var image in images)
        {
            note("INFO", $"Checking {image}'s Python libraries for known vulnerabilities");
            // By its id, so the containers made from it next find its libraries already read.
            var found = await CheckAsync(image, (await OriginAsync(image, cancellationToken)).Id, note, cancellationToken);
            foreach (var vulnerability in found)
            {
                note("WARNING", $"{image}: {vulnerability}");
            }

            stopping.AddRange(found.Where(v => v.Blocks).Select(v => (image, v)));
        }

        if (stopping.Count == 0)
        {
            return;
        }

        if (policy == ImageSecurityPolicy.Block)
        {
            // Each vulnerability is a line above; what is to be done is said once per library, a line each.
            var libraries = stopping.GroupBy(s => (s.Image, s.Vulnerability.Package, s.Vulnerability.Version)).ToList();
            foreach (var library in libraries)
            {
                note("ERROR", Remedy(library.Key.Image, [.. library.Select(s => s.Vulnerability)]));
            }

            throw new InvalidOperationException(
                $"{libraries.Count} Python librar{(libraries.Count == 1 ? "y" : "ies")} with a vulnerability moderate or worse, so no container was made. "
                + "Raise each to the version named, replace the ones no version fixes, and build again; Settings → Security → Image security check set to Warn builds anyway.");
        }

        note("WARNING", $"{stopping.Count} vulnerabilit{(stopping.Count == 1 ? "y" : "ies")} moderate or worse; built anyway, as Settings → Security → Image security check is set to Warn.");
    }

    /// <summary>
    /// Before a container is made from <paramref name="image"/> (Run, Create,
    /// View &amp; edit's recreate, an image update, a project's save): its
    /// Python libraries asked of OSV.dev as after a build, since what was clean
    /// when it was built may not be today. An image built on this machine with
    /// one moderate or worse is refused under <see cref="ImageSecurityPolicy.Block"/>
    /// (<see cref="InvalidOperationException"/>): no container is made, and the
    /// caller has touched nothing yet, so a container it was to replace runs on
    /// as it was. An image from a registry is never refused, since it is not
    /// ours to mend: what it carries comes back as notes for the user, as
    /// Warn's do. Containers already made are never stopped by the check.
    /// </summary>
    public async Task<(IReadOnlyList<string> Said, string Worst)> GuardLaunchAsync(string image, CancellationToken cancellationToken)
    {
        var policy = settings.Get().Policy;
        image = image.Trim();
        if (policy == ImageSecurityPolicy.Off || image.Length == 0)
        {
            return ([], "");
        }

        // Not on the machine: the run pulls it, from a registry, and it is not ours to stop.
        var (built, remedies, worst) = await RemediesAsync(image, "before a container", cancellationToken);
        if (remedies.Count == 0)
        {
            return ([], "");
        }

        if (built && policy == ImageSecurityPolicy.Block)
        {
            var first = $"{image} was built on this machine and has Python libraries with a vulnerability moderate or worse, so no container was made from it; "
                + "a container it was to replace runs on as it was.";
            throw new ImageRefusedException(
                first + "\n\n" + string.Join("\n", remedies)
                + "\n\nRaise each to the version named, replace the ones no version fixes, and build the image again; Settings → Security → Image security check set to Warn makes it anyway.",
                [first, .. remedies], worst);
        }

        return (
        [
            built
                ? $"{image} has Python libraries with a vulnerability moderate or worse; made anyway, as Settings → Security → Image security check is set to Warn."
                : $"{image} comes from a registry and has Python libraries with a vulnerability moderate or worse. It is not built here, so it is not stopped: a newer version of it may mend them.",
            .. remedies,
        ], worst);
    }

    /// <summary>
    /// Before a container that exists is started (Start, Restart, a project's
    /// start, its restart policy): its image checked as a new container's is.
    /// One built on this machine with a vulnerability moderate or worse is not
    /// started under <see cref="ImageSecurityPolicy.Block"/>
    /// (<see cref="ImageRefusedException"/>): it is ours to mend, and is built
    /// again. One from a registry always starts, since it is not ours to mend:
    /// whether it runs is the user's to decide, and it is marked instead
    /// (<see cref="ImageWarnings"/>). A container that runs is never stopped by
    /// the check, so a start of one that runs passes; a <paramref name="restart"/>,
    /// which would stop it first, is refused before anything is touched, and it
    /// runs on. Nothing when the container cannot be read: its start says why.
    /// </summary>
    public async Task GuardStartAsync(string container, CancellationToken cancellationToken, string? session = null, bool restart = false)
    {
        if (settings.Get().Policy != ImageSecurityPolicy.Block)
        {
            return;
        }

        Containers.ContainerInspection inspected;
        try
        {
            inspected = Containers.ContainerInspection.Parse(
                await wslc.RunAsync(["container", "inspect", container, "--format", "json"], cancellationToken: cancellationToken, session: session));
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException or JsonException)
        {
            return;
        }

        if (inspected.IsRunning && !restart)
        {
            return;
        }

        var image = inspected.Image;
        var (built, remedies, worst) = await RemediesAsync(image, $"before container {container} starts", cancellationToken);
        if (!built || remedies.Count == 0)
        {
            return;
        }

        var first = $"{image} was built on this machine and has Python libraries with a vulnerability moderate or worse, so container {inspected.Name} was not {(restart ? "restarted; it runs on as it was" : "started")}.";
        throw new ImageRefusedException(
            first + "\n\n" + string.Join("\n", remedies)
            + "\n\nRaise each to the version named, replace the ones no version fixes, build the image again and save the container or its project again; "
            + "Settings → Security → Image security check set to Warn starts it anyway.",
            [first, .. remedies], worst, inspected.Id, inspected.Name);
    }

    /// <summary>
    /// A container the agent did not make (<c>wslc</c> run around it, a
    /// script's), or made before the check was: its image checked as a new
    /// container's is, after the fact. Nothing is refused, since it is made
    /// already and was not the agent's to stop: what it carries comes back
    /// for the container to be marked with (<see cref="ImageWarnings"/>).
    /// None when the check is off, the image is not on the machine, or it is clean.
    /// </summary>
    public async Task<(IReadOnlyList<string> Said, bool Refused, string Worst)> AuditAsync(string image, string container, CancellationToken cancellationToken)
    {
        image = image.Trim();
        var policy = settings.Get().Policy;
        if (policy == ImageSecurityPolicy.Off || image.Length == 0)
        {
            return ([], false, "");
        }

        var (built, remedies, worst) = await RemediesAsync(image, $"of container {container}, already made", cancellationToken);
        return remedies.Count == 0 ? ([], false, "") : (
        [
            built
                ? $"{image} was built on this machine and has Python libraries with a vulnerability moderate or worse. Container {container} was made without its image being checked (around the agent, or before the check was), so nothing stopped it; it runs on."
                : $"{image} comes from a registry and has Python libraries with a vulnerability moderate or worse. It is not built here, so container {container} is not stopped: a newer version of the image may mend them.",
            .. remedies,
        ], built && policy == ImageSecurityPolicy.Block, worst);
    }

    /// <summary>
    /// What a container of the image would carry: whether it was built here,
    /// and a line per library with a vulnerability moderate or worse
    /// (<see cref="Remedy"/>), each written in the log as a warning saying
    /// <paramref name="when"/> it was found, and the worst severity among them.
    /// None for an image not on the machine.
    /// </summary>
    private async Task<(bool Built, List<string> Remedies, string Worst)> RemediesAsync(string image, string when, CancellationToken cancellationToken)
    {
        var (id, built) = await OriginAsync(image, cancellationToken);
        if (id is null)
        {
            return (false, [], "");
        }

        var found = await CheckAsync(image, id, (level, text) =>
        {
            if (level == "WARNING")
            {
                logger.LogWarning("image security check {When} of {Image}: {Text}", when, image, text);
            }
        }, cancellationToken);
        var remedies = found.Where(v => v.Blocks).GroupBy(v => (v.Package, v.Version)).Select(library => Remedy(image, [.. library])).ToList();
        foreach (var remedy in remedies)
        {
            logger.LogWarning("image security check {When} of {Image}: {Remedy}", when, image, remedy);
        }

        var worst = found.Where(v => v.Blocks).Select(v => v.Severity).MaxBy(severity => Array.IndexOf(SeverityOrder, severity)) ?? "";
        return (built, remedies, worst);
    }

    /// <summary>
    /// The image's id, and whether it was built on this machine: an image
    /// pulled from a registry carries the digest it was pulled by, and one
    /// built here has none. A null id is an image not on the machine.
    /// </summary>
    private async Task<(string? Id, bool Built)> OriginAsync(string image, CancellationToken cancellationToken)
    {
        try
        {
            var result = await wslc.RunAsync(["image", "inspect", image], cancellationToken: cancellationToken);
            var inspected = JsonNode.Parse(result.Stdout) switch
            {
                JsonArray array => array.FirstOrDefault(),
                var single => single,
            };
            var id = (string?)inspected?["Id"];
            return string.IsNullOrWhiteSpace(id) ? (null, false) : (id, (inspected?["RepoDigests"]?.AsArray() ?? []).Count == 0);
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException or JsonException)
        {
            return (null, false);
        }
    }

    /// <summary>
    /// One library's line: how many of its vulnerabilities stop the build, the
    /// worst of them, and the version that fixes them all (the highest of their
    /// fixes), or, when one has no fix, that the library has to be replaced.
    /// </summary>
    internal static string Remedy(string image, IReadOnlyList<ImageVulnerability> vulnerabilities)
    {
        var first = vulnerabilities[0];
        var worst = vulnerabilities.MaxBy(v => Array.IndexOf(SeverityOrder, v.Severity))!.Severity;
        var counted = vulnerabilities.Count == 1 ? "1 vulnerability" : $"{vulnerabilities.Count} vulnerabilities";
        var unfixed = vulnerabilities.FirstOrDefault(v => v.FixedIn.Length == 0);
        var remedy = unfixed is not null
            ? $"no version fixes {unfixed.Advisory} yet: replace it"
            : $"{vulnerabilities.Select(v => v.FixedIn).Order(VersionOrder.Instance).Last()} fixes {(vulnerabilities.Count == 1 ? "it" : "them all")}";
        return $"{image}: {first.Package} {first.Version}: {counted} moderate or worse{WorstSaid}{worst}; {remedy}.";
    }

    /// <summary>How a <see cref="Remedy"/> line says its worst severity, written there and read back by <see cref="LogLevelOf"/>.</summary>
    private const string WorstSaid = ", the worst ";

    /// <summary>
    /// The level a line of the image check is written in at the end of a
    /// log: ERROR, in red, for a library whose worst vulnerability is high or
    /// critical, as the state chip says it; WARNING for any other line.
    /// </summary>
    public static string LogLevelOf(string line)
    {
        var at = line.IndexOf(WorstSaid, StringComparison.Ordinal);
        if (at < 0)
        {
            return "WARNING";
        }

        var severity = line[(at + WorstSaid.Length)..].Split(';', 2)[0];
        return severity is "high" or "critical" ? "ERROR" : "WARNING";
    }

    /// <summary>Versions in the order PyPI releases them, closely enough for picking the highest fix: number by number, a part that is not one compared as text.</summary>
    private sealed class VersionOrder : IComparer<string>
    {
        public static readonly VersionOrder Instance = new();

        public int Compare(string? x, string? y)
        {
            var left = (x ?? "").Split('.');
            var right = (y ?? "").Split('.');
            for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
            {
                var a = i < left.Length ? left[i] : "0";
                var b = i < right.Length ? right[i] : "0";
                var order = long.TryParse(a, out var na) && long.TryParse(b, out var nb)
                    ? na.CompareTo(nb)
                    : string.CompareOrdinal(a, b);
                if (order != 0)
                {
                    return order;
                }
            }

            return 0;
        }
    }

    /// <summary>The image's vulnerabilities; none when it has no Python, or when it could not be checked (said through <paramref name="note"/>).</summary>
    /// <summary>
    /// What View image packages and CVEs shows: every library of the image and
    /// every known vulnerability of them, the low ones too, the worst first,
    /// with the lines a refusal would give. Nothing is refused nor marked by
    /// asking, whatever Settings → Security says; it is only read.
    /// </summary>
    public async Task<ImageVulnerabilities> ReportAsync(string image, CancellationToken cancellationToken = default)
    {
        image = image.Trim();
        var (id, built) = await OriginAsync(image, cancellationToken);
        if (id is null)
        {
            return new ImageVulnerabilities(image, false, false, false, false, false, [], [], [], $"{image} is not on this machine.");
        }

        List<(string Name, string Version)>? packages;
        try
        {
            packages = await PackagesAsync(image, id, cancellationToken);
        }
        catch (Exception ex) when (ex is TimeoutException or WslcNotFoundException or WslcException or IOException or InvalidDataException)
        {
            return new ImageVulnerabilities(image, true, built, false, false, false, [], [], [], $"{image} could not be read ({ex.Message}).");
        }

        if (packages is null)
        {
            return new ImageVulnerabilities(image, true, built, false, true, false, [], [], [], $"{image} has no Python libraries: nothing to check.");
        }

        var listed = packages.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(p => new ImagePackage(p.Name, p.Version)).ToList();
        List<ImageVulnerability> found;
        try
        {
            found = await OsvAsync(packages, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new ImageVulnerabilities(image, true, built, true, false, false, listed, [], [], $"OSV.dev could not be reached ({ex.Message}): the libraries are listed unchecked.");
        }

        var remedies = found.Where(v => v.Blocks).GroupBy(v => (v.Package, v.Version)).Select(library => Remedy(image, [.. library])).ToList();
        var advisories = found
            .OrderByDescending(v => Array.IndexOf(SeverityOrder, v.Severity))
            .ThenBy(v => v.Package, StringComparer.OrdinalIgnoreCase)
            .Select(v => new ImageAdvisory(v.Package, v.Version, v.Advisory, v.Cve, v.Severity, v.FixedIn, v.Url))
            .ToList();
        return new ImageVulnerabilities(image, true, built, true, true, built && remedies.Count > 0 && settings.Get().Policy == ImageSecurityPolicy.Block,
            listed, advisories, remedies);
    }

    /// <summary>The image's Python libraries, from what was read under its id already, or read now and kept; null for an image with no Python.</summary>
    private async Task<List<(string Name, string Version)>?> PackagesAsync(string image, string? id, CancellationToken cancellationToken)
    {
        if (id is not null && (_packages.TryGetValue(id, out var kept) || Kept(id, out kept)))
        {
            _packages[id] = kept;
            return kept;
        }

        var packages = await PythonPackagesAsync(image, cancellationToken);
        if (id is not null)
        {
            _packages[id] = packages;
            Keep(id, packages);
        }

        return packages;
    }

    /// <summary>What an image's file in <c>keptIn</c> says: its libraries, null for none; false when it has no file, or one that cannot be read.</summary>
    private bool Kept(string id, out List<(string Name, string Version)>? packages)
    {
        packages = null;
        if (KeptPath(id) is not { } path || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var kept = JsonSerializer.Deserialize<KeptPackages>(File.ReadAllText(path), KeptJson);
            if (kept is null)
            {
                return false;
            }

            packages = kept.Packages?.Select(p => (p.Name, p.Version)).ToList();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning("image security check: {Path} could not be read, the image is read again: {Message}", path, ex.Message);
            return false;
        }
    }

    private void Keep(string id, List<(string Name, string Version)>? packages)
    {
        if (KeptPath(id) is not { } path)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new KeptPackages(packages?.Select(p => new KeptPackage(p.Name, p.Version)).ToList()), KeptJson));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("image security check: {Path} could not be written, the image will be read again after a restart: {Message}", path, ex.Message);
        }
    }

    /// <summary>The image's file in <c>keptIn</c>, named by its id (<c>sha256-…</c>); null when nothing is kept on disk.</summary>
    private string? KeptPath(string id) =>
        keptIn is null ? null : Path.Combine(keptIn, $"{id.Replace(':', '-')}.json");

    private static readonly JsonSerializerOptions KeptJson = new(JsonSerializerDefaults.Web);

    /// <summary>An image's libraries as kept on disk; null for an image with no Python.</summary>
    private sealed record KeptPackages(List<KeptPackage>? Packages);

    private sealed record KeptPackage(string Name, string Version);

    private async Task<List<ImageVulnerability>> CheckAsync(string image, string? id, Action<string, string> note, CancellationToken cancellationToken)
    {
        List<(string Name, string Version)>? packages;
        try
        {
            packages = await PackagesAsync(image, id, cancellationToken);
        }
        catch (Exception ex) when (ex is TimeoutException or WslcNotFoundException or WslcException or IOException or InvalidDataException)
        {
            note("WARNING", $"{image} could not be read ({ex.Message}): not checked.");
            return [];
        }

        if (packages is null)
        {
            note("INFO", $"{image} has no Python libraries: nothing to check.");
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
    /// The image's Python libraries, read from its files and never by running
    /// it: anything run in an image runs its code (a Python reads its own
    /// sitecustomize and .pth files as it starts), and a scanner that runs what
    /// it scans is a way in. A container of it is made and never started, its
    /// files exported to a temporary archive, and every installed library's
    /// metadata read from it (<c>*.dist-info/METADATA</c>, <c>*.egg-info</c>),
    /// in every site-packages it has, a virtual environment's too; then both
    /// are let go. Null when it has no Python library.
    /// </summary>
    private async Task<List<(string Name, string Version)>?> PythonPackagesAsync(string image, CancellationToken cancellationToken)
    {
        var name = $"{CheckPrefix}{Guid.NewGuid():N}"[..32];
        var archive = Path.Combine(Path.GetTempPath(), $"{name}.tar");
        try
        {
            // A command is given for an image that names none; the container is never started.
            await wslc.RunAsync(["container", "create", "--name", name, "--pull", "never", image, "true"], ListTimeout, cancellationToken);
            await wslc.RunAsync(["container", "export", "--output", archive, name], ListTimeout, cancellationToken);
            return await InstalledAsync(archive, cancellationToken);
        }
        finally
        {
            await RemoveAsync(name);
            try
            {
                File.Delete(archive);
            }
            catch (IOException ex)
            {
                logger.LogWarning("image security check: {Archive} could not be deleted: {Message}", archive, ex.Message);
            }
        }
    }

    private async Task RemoveAsync(string container)
    {
        try
        {
            await wslc.RunAsync(["container", "rm", "--force", container]);
        }
        catch (Exception ex) when (ex is WslcException or TimeoutException or WslcNotFoundException)
        {
            // Never created (the image could not be read), or already gone.
        }
    }

    /// <summary>
    /// Every installed library an exported filesystem holds, by its metadata:
    /// a <c>Name:</c> and a <c>Version:</c> among the headers each library's
    /// METADATA (or PKG-INFO) begins with, in a site-packages or dist-packages
    /// folder; each name and version once. Null when there is none. An
    /// installer may keep the file elsewhere and link it into site-packages
    /// (uv links from its cache): a hard link in the archive carries no data,
    /// only the path it is to, so every metadata file is kept as it comes, and
    /// a link is read through the one it names.
    /// </summary>
    internal static async Task<List<(string Name, string Version)>?> InstalledAsync(string archive, CancellationToken cancellationToken)
    {
        var found = new HashSet<(string Name, string Version)>();
        var seen = new Dictionary<string, (string Name, string Version)?>(StringComparer.Ordinal);
        await using var file = File.OpenRead(archive);
        await using var reader = new TarReader(file);
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
        {
            if (!IsMetadataFile(entry.Name))
            {
                continue;
            }

            var library = entry.EntryType == TarEntryType.HardLink
                ? seen.GetValueOrDefault(Normalized(entry.LinkName))
                : entry.DataStream is { } data ? await HeadersAsync(data, cancellationToken) : null;
            seen[Normalized(entry.Name)] = library;
            if (library is { } installed && IsInstalled(entry.Name))
            {
                found.Add(installed);
            }
        }

        return found.Count == 0 ? null : [.. found.OrderBy(library => library.Name, StringComparer.OrdinalIgnoreCase)];

        // An archive names a link's target as it names its entries, with or without a leading ./ or /.
        static string Normalized(string path) => path.TrimStart('.').TrimStart('/');
    }

    /// <summary>The <c>Name:</c> and <c>Version:</c> of a metadata file; the headers end at the first empty line, and the description after it is not read.</summary>
    private static async Task<(string Name, string Version)?> HeadersAsync(Stream data, CancellationToken cancellationToken)
    {
        string? name = null, version = null;
        using var text = new StreamReader(data);
        while (await text.ReadLineAsync(cancellationToken) is { Length: > 0 } line)
        {
            if (line.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
            {
                name = line[5..].Trim();
            }
            else if (line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
            {
                version = line[8..].Trim();
            }
        }

        return name is { Length: > 0 } && version is { Length: > 0 } ? (name, version) : null;
    }

    /// <summary>A library's metadata wherever it is: <c>x-1.0.dist-info/METADATA</c>, <c>x.egg-info/PKG-INFO</c>, or an <c>x.egg-info</c> file.</summary>
    private static bool IsMetadataFile(string path) =>
        path.EndsWith(".dist-info/METADATA", StringComparison.Ordinal)
        || path.EndsWith(".egg-info/PKG-INFO", StringComparison.Ordinal)
        || path.EndsWith(".egg-info", StringComparison.Ordinal);

    /// <summary>Installed where Python imports from: a site-packages or dist-packages folder, not an installer's cache.</summary>
    private static bool IsInstalled(string path) => path.Contains("-packages/", StringComparison.Ordinal);

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
            var idsOf = results.Take(batch.Length)
                .Select(result => (result?["vulns"]?.AsArray() ?? []).Select(v => (string?)v?["id"]).OfType<string>().ToList())
                .ToList();

            // Read side by side: one by one, the hundred advisories of an old image took half a
            // minute, which a View & edit's Save waited through.
            var read = new ConcurrentDictionary<string, JsonNode?>(StringComparer.Ordinal);
            await Parallel.ForEachAsync(idsOf.SelectMany(ids => ids).Distinct(),
                new ParallelOptions { MaxDegreeOfParallelism = AdvisoriesAtOnce, CancellationToken = cancellationToken },
                async (id, stop) => read[id] = await AdvisoryAsync(id, stop));

            for (var i = 0; i < idsOf.Count; i++)
            {
                var ids = idsOf[i];
                var advisories = ids.Select(id => (Id: id, Advisory: read[id])).ToList();

                // One vulnerability is often both a PYSEC and a GHSA advisory, each naming the other: the GHSA one is kept, it carries the severity.
                foreach (var (id, advisory) in advisories.Where(a => a.Id.StartsWith("GHSA-", StringComparison.Ordinal)
                    || !Aliases(a.Advisory).Any(alias => ids.Contains(alias))))
                {
                    found.Add(await ReadAsync(id, advisory, batch[i], cancellationToken));
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

    /// <summary>
    /// One advisory of one library. An advisory that gives no severity (a PYSEC
    /// one, whose GHSA twin OSV.dev did not list) takes its GHSA alias's, or
    /// else the one its CVSS vector scores: left unknown, a critical one would
    /// not stop the build.
    /// </summary>
    private async Task<ImageVulnerability> ReadAsync(string id, JsonNode? advisory, (string Name, string Version) package, CancellationToken cancellationToken)
    {
        var read = Read(id, advisory, package);
        if (read.Severity != Unknown)
        {
            return read;
        }

        foreach (var alias in Aliases(advisory).Where(alias => alias.StartsWith("GHSA-", StringComparison.Ordinal)))
        {
            try
            {
                if (Severity(await AdvisoryAsync(alias, cancellationToken)) is var named && named != Unknown)
                {
                    return read with { Severity = named };
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                // An alias OSV.dev does not serve: the vector is asked next.
            }
        }

        var vectors = (advisory?["severity"]?.AsArray() ?? [])
            .Where(s => ((string?)s?["type"] ?? "").StartsWith("CVSS_V3", StringComparison.Ordinal))
            .Select(s => (string?)s?["score"]).OfType<string>();
        return read with { Severity = vectors.Select(CvssSeverity).FirstOrDefault(s => s != Unknown) ?? Unknown };
    }

    private static ImageVulnerability Read(string id, JsonNode? advisory, (string Name, string Version) package)
    {
        var aliases = Aliases(advisory).ToList();
        var fixedIn = (advisory?["affected"]?.AsArray() ?? [])
            .Where(a => Normalized((string?)a?["package"]?["name"]) == Normalized(package.Name))
            .SelectMany(a => a?["ranges"]?.AsArray() ?? [])
            .SelectMany(r => r?["events"]?.AsArray() ?? [])
            .Select(e => (string?)e?["fixed"]).OfType<string>()
            .FirstOrDefault() ?? "";
        return new ImageVulnerability(package.Name, package.Version, id, aliases.FirstOrDefault(a => a.StartsWith("CVE-", StringComparison.Ordinal)) ?? "",
            Severity(advisory), fixedIn, $"https://osv.dev/vulnerability/{id}");
    }

    /// <summary>The severity the advisory's database gives it (GitHub's: low, moderate or medium, high, critical).</summary>
    private static string Severity(JsonNode? advisory) =>
        ((string?)advisory?["database_specific"]?["severity"] ?? "").Trim().ToLowerInvariant() switch
        {
            "medium" => "moderate",
            var known when known is "low" or "moderate" or "high" or "critical" => known,
            _ => Unknown,
        };

    /// <summary>
    /// The severity a CVSS 3.x vector scores, by its base score as the
    /// specification computes it (FIRST, CVSS v3.1, section 7): under 4 low,
    /// under 7 moderate, under 9 high, else critical; a vector that cannot be
    /// read, or that scores 0, is unknown.
    /// </summary>
    internal static string CvssSeverity(string vector)
    {
        var metrics = vector.Split('/').Select(part => part.Split(':')).Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
        string Metric(string name) => metrics.GetValueOrDefault(name, "");

        var changed = Metric("S") == "C";
        double? av = Metric("AV") switch { "N" => 0.85, "A" => 0.62, "L" => 0.55, "P" => 0.2, _ => null };
        double? ac = Metric("AC") switch { "L" => 0.77, "H" => 0.44, _ => null };
        double? pr = Metric("PR") switch { "N" => 0.85, "L" => changed ? 0.68 : 0.62, "H" => changed ? 0.5 : 0.27, _ => null };
        double? ui = Metric("UI") switch { "N" => 0.85, "R" => 0.62, _ => null };
        double? Impact(string name) => Metric(name) switch { "H" => 0.56, "L" => 0.22, "N" => 0, _ => null };
        if (av is null || ac is null || pr is null || ui is null || Impact("C") is not { } c || Impact("I") is not { } i || Impact("A") is not { } a
            || Metric("S") is not ("U" or "C"))
        {
            return Unknown;
        }

        var iss = 1 - (1 - c) * (1 - i) * (1 - a);
        var impact = changed ? 7.52 * (iss - 0.029) - 3.25 * Math.Pow(iss - 0.02, 15) : 6.42 * iss;
        if (impact <= 0)
        {
            return Unknown;
        }

        var exploitability = 8.22 * av.Value * ac.Value * pr.Value * ui.Value;
        var score = RoundUp(Math.Min((changed ? 1.08 : 1) * (impact + exploitability), 10));
        return score switch { < 4 => "low", < 7 => "moderate", < 9 => "high", _ => "critical" };
    }

    /// <summary>The specification's Roundup: up to one decimal, read through whole numbers so 4.0000001 is not 4.1.</summary>
    private static double RoundUp(double value)
    {
        var whole = (long)Math.Round(value * 100000);
        return whole % 10000 == 0 ? whole / 100000.0 : (Math.Floor(whole / 10000.0) + 1) / 10.0;
    }

    /// <summary>PyPI's names compare as PEP 503 says: case and <c>_</c>, <c>-</c>, <c>.</c> alike.</summary>
    private static string Normalized(string? name) => (name ?? "").ToLowerInvariant().Replace('_', '-').Replace('.', '-');
}
