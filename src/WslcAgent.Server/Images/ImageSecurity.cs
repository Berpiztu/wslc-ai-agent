using System.Collections.Concurrent;
using System.Formats.Tar;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Host;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>Settings → Security → Image security check, kept in <c>image-security.json</c>; blocking until changed.</summary>
public sealed class ImageSecuritySettingsStore(IOptions<WslcOptions> options)
    : SavedSettings<ImageSecuritySettings>(Path.Combine(options.Value.DataDirectory, "image-security.json"), new ImageSecuritySettings());

/// <summary>A known vulnerability of one package in an image: a Python library, or a system package of its distribution (<see cref="Ecosystem"/>).</summary>
/// <param name="Severity"><c>low</c>, <c>moderate</c>, <c>high</c>, <c>critical</c>, or <c>unknown</c> when neither the advisory, its GHSA alias nor its CVSS vector gives one.</param>
/// <param name="FixedIn">The first version that fixes it; empty when none does yet.</param>
/// <param name="UbuntuProFixedIn">The version only Ubuntu Pro fixes it in, when <paramref name="FixedIn"/> is empty: said, never installed.</param>
public sealed record ImageVulnerability(string Package, string Version, string Advisory, string Cve, string Severity, string FixedIn, string Url, string Ecosystem = "PyPI", string UbuntuProFixedIn = "")
{
    /// <summary>The package as a line names it: a Python library by its name, a system package with its distribution (<c>openssl (Debian:12)</c>).</summary>
    public string Named => Ecosystem == Installed.PyPI ? Package : $"{Package} ({Ecosystem})";

    /// <summary>Moderate or worse: what stops a build under <see cref="ImageSecurityPolicy.Block"/>.</summary>
    public bool Blocks => Severity is "moderate" or "high" or "critical";

    public override string ToString() =>
        $"{Named} {Version}, {Severity}: {Advisory}{(Cve.Length > 0 ? $" ({Cve})" : "")}, {ImageRemedies.FixSaid(FixedIn, UbuntuProFixedIn)}";

    /// <summary>The advisory as the API gives it.</summary>
    public ImageAdvisory ToAdvisory() => new(Package, Version, Advisory, Cve, Severity, FixedIn, Url, Ecosystem, UbuntuProFixedIn);
}

/// <summary>
/// One package installed in an image: a Python library (<see cref="PyPI"/>), or
/// a system package of its distribution, in OSV.dev's name for it
/// (<c>Alpine:v3.20</c>, <c>Debian:12</c>, <c>Ubuntu:22.04</c>), named as
/// OSV.dev knows it: Alpine's by its origin, Debian's and Ubuntu's by their
/// source package.
/// </summary>
internal readonly record struct Installed(string Ecosystem, string Name, string Version)
{
    public const string PyPI = "PyPI";

    /// <summary>Not a package: a <c>requirements*.txt</c> file of the image, its path as <see cref="Name"/> and its text as <see cref="Version"/>, read and kept with the packages so a patch can be told what it pins.</summary>
    public const string RequirementsFile = "requirements";

    /// <summary>
    /// Not a package: whether an Ubuntu image is attached to Ubuntu Pro, its
    /// <see cref="Name"/> <see cref="Attached"/> or <see cref="Free"/>, read and
    /// kept with the packages, so a fix only Pro carries is known to be one its
    /// upgrade reaches, or not.
    /// </summary>
    public const string UbuntuPro = "ubuntu-pro";

    public const string Attached = "attached";

    public const string Free = "free";

    /// <summary>Not a package: the rule the packages were read by (<see cref="ImageSecurity.ReadRule"/>), kept with them, so packages kept by an earlier one are read again.</summary>
    public const string ReadBy = "read-by";
}

/// <summary>
/// The check every image the agent builds goes through before a container is
/// made from it (<see cref="ImageSecuritySettings"/>): the image's Python
/// libraries and its system packages (Alpine's apk, Debian's and Ubuntu's
/// dpkg), read from the image's own files and never by running it, so the
/// versions are the ones it carries whatever its requirements pin, asked of
/// OSV.dev, each package once. An image with neither is said to be unchecked,
/// never clean. The
/// check is a safety net, not the build: when OSV.dev cannot be reached, or
/// the image cannot be read, it says so in the log and the build goes on.
/// It is made again before every new container (<see cref="GuardLaunchAsync"/>),
/// since an advisory found after the build is no less there; a container that
/// exists is started and stopped whatever its image carries. What every check
/// finds is kept on the image (<paramref name="marks"/>, <see cref="ImageMarks"/>),
/// which its containers read.
/// <paramref name="keptIn"/>: the folder where each image's libraries are kept
/// once read, by its id, so a restart of the agent does not read a large image
/// again; <paramref name="advisoriesIn"/>, where each advisory read from OSV.dev
/// is kept for a day, so Zaindari's window opens at once after a restart too.
/// None, and they are kept only while it runs.
/// </summary>
public sealed partial class ImageSecurity(
    IWslcRunner wslc,
    HttpClient http,
    ImageSecuritySettingsStore settings,
    ILogger<ImageSecurity> logger,
    string? keptIn = null,
    string? advisoriesIn = null,
    ImageMarks? marks = null)
    : WslcAgent.Mcp.IImageSecurity
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
    /// The packages of each image read so far, by the image's id: an
    /// image never changes under its id, so a second container made from it
    /// costs no second reading of its files, nor does a restart of the agent
    /// (<c>keptIn</c>); null for an image with no package to read. What the
    /// libraries are vulnerable to is asked anew each time.
    /// </summary>
    private readonly ConcurrentDictionary<string, List<Installed>?> _packages = new(StringComparer.Ordinal);

    /// <summary>
    /// Checks the images just built, writing what it does and finds through
    /// <paramref name="note"/> (a level and a text, as a job's log lines go).
    /// Under <see cref="ImageSecurityPolicy.Block"/>, an image with a
    /// vulnerability moderate or worse throws <see cref="InvalidOperationException"/>,
    /// naming each one: the job fails before any container is made from it. A
    /// patch Zaindari built (<c>…-patched</c>) is never refused: it is said.
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
            note("INFO", $"Checking {image}'s Python libraries and system packages for known vulnerabilities");
            // By its id, so the containers made from it next find its libraries already read.
            var (id, built, _) = await OriginAsync(image, cancellationToken);
            var found = await CheckAsync(image, id, built, note, cancellationToken);
            foreach (var vulnerability in found)
            {
                note("WARNING", $"{image}: {vulnerability}");
            }

            if (!ImageReference.IsPatched(image))
            {
                stopping.AddRange(found.Where(v => v.Blocks).Select(v => (image, v)));
            }
            else if (found.Any(v => v.Blocks))
            {
                // A patch mends what has a fix published; what is left is its original's, and refusing
                // the patch would leave the containers on the original, which carries all of it.
                note("WARNING", $"{image} is a patch, so it is not refused: what is left is its original's to mend. "
                    + ImageRemedies.WhatIsLeft([.. found.Select(v => v.ToAdvisory())]));
            }
        }

        if (stopping.Count == 0)
        {
            return;
        }

        if (policy == ImageSecurityPolicy.Block)
        {
            // Each vulnerability is a line above; what is to be done is said once per library, a line each.
            var libraries = stopping.GroupBy(s => (s.Image, s.Vulnerability.Ecosystem, s.Vulnerability.Package, s.Vulnerability.Version)).ToList();
            foreach (var library in libraries)
            {
                note("ERROR", Remedy(library.Key.Image, [.. library.Select(s => s.Vulnerability)]));
            }

            throw new InvalidOperationException(
                $"{libraries.Count} package{(libraries.Count == 1 ? "" : "s")} with a vulnerability moderate or worse, so no container was made. "
                + "Raise each to the version named, replace the ones no version fixes, and build again; Settings → Security → Image security check set to Warn builds anyway.");
        }

        note("WARNING", $"{stopping.Count} vulnerabilit{(stopping.Count == 1 ? "y" : "ies")} moderate or worse; built anyway, as Settings → Security → Image security check is set to Warn.");
    }

    /// <summary>
    /// Before a container is made from <paramref name="image"/> (Run, Create,
    /// View &amp; edit's recreate, an image update, a project's save): its
    /// packages asked of OSV.dev as after a build, since what was clean
    /// when it was built may not be today. An image built on this machine with
    /// one moderate or worse is refused under <see cref="ImageSecurityPolicy.Block"/>
    /// (<see cref="InvalidOperationException"/>): no container is made, and the
    /// caller has touched nothing yet, so a container it was to replace runs on
    /// as it was. An image from a registry is never refused, since it is not
    /// ours to mend: what it carries comes back as notes for the user, as
    /// Warn's do. Containers already made are never stopped by the check.
    /// </summary>
    public async Task<IReadOnlyList<string>> GuardLaunchAsync(string image, CancellationToken cancellationToken)
    {
        var policy = settings.Get().Policy;
        image = image.Trim();
        if (policy == ImageSecurityPolicy.Off || image.Length == 0)
        {
            return [];
        }

        // Not on the machine: the run pulls it, from a registry, and it is not ours to stop.
        var (built, remedies) = await RemediesAsync(image, "before a container", cancellationToken);
        if (remedies.Count == 0)
        {
            return [];
        }

        if (built && policy == ImageSecurityPolicy.Block)
        {
            throw new InvalidOperationException(
                $"{image} was built on this machine and has packages with a vulnerability moderate or worse, so no container was made from it; "
                + "a container it was to replace runs on as it was.\n\n"
                + string.Join("\n", remedies)
                + "\n\nRaise each to the version named, replace the ones no version fixes, and build the image again; Settings → Security → Image security check set to Warn makes it anyway.");
        }

        return
        [
            built
                ? $"{image} has packages with a vulnerability moderate or worse; made anyway, as Settings → Security → Image security check is set to Warn."
                : $"{image} comes from a registry and has packages with a vulnerability moderate or worse. It is not built here, so it is not stopped: a newer version of it may mend them.",
            .. remedies,
        ];
    }

    /// <summary>
    /// The image of a container the agent did not make (<c>wslc</c> run around
    /// it, a script's), or made before the check was, checked after the fact:
    /// nothing is refused, since the container is made already, and what is
    /// found is kept on the image (<see cref="ImageMarks"/>), which the
    /// container reads. Nothing when the check is off.
    /// </summary>
    public async Task MarkAsync(string image, CancellationToken cancellationToken)
    {
        image = image.Trim();
        if (settings.Get().Policy != ImageSecurityPolicy.Off && image.Length > 0)
        {
            await RemediesAsync(image, "of a container's image", cancellationToken);
        }
    }

    /// <summary>
    /// What a container of the image would carry: whether it was built here,
    /// and a line per library with a vulnerability moderate or worse
    /// (<see cref="Remedy"/>), each written in the log as a warning saying
    /// <paramref name="when"/> it was found. None for an image not on the machine.
    /// </summary>
    private async Task<(bool Built, List<string> Remedies)> RemediesAsync(string image, string when, CancellationToken cancellationToken)
    {
        var (id, built, _) = await OriginAsync(image, cancellationToken);
        if (id is null)
        {
            return (false, []);
        }

        var found = await CheckAsync(image, id, built, (level, text) =>
        {
            if (level == "WARNING")
            {
                logger.LogWarning("image security check {When} of {Image}: {Text}", when, image, text);
            }
        }, cancellationToken);
        var remedies = found.Where(v => v.Blocks).GroupBy(v => (v.Ecosystem, v.Package, v.Version)).Select(library => Remedy(image, [.. library])).ToList();
        foreach (var remedy in remedies)
        {
            logger.LogWarning("image security check {When} of {Image}: {Remedy}", when, image, remedy);
        }

        return (built, remedies);
    }

    /// <summary>
    /// The image's id, and whether it was built on this machine: an image
    /// pulled from a registry carries the digest it was pulled by, and one
    /// built here has none. A patch Zaindari built (<c>…-patched</c>) is built
    /// here but never counted as built: it was made to mend what could be
    /// mended, and what is left in it, a fix nobody has published, is its
    /// original's to mend; refusing it would leave the original, which is
    /// worse. A null id is an image not on the machine.
    /// </summary>
    private async Task<(string? Id, bool Built, string User)> OriginAsync(string image, CancellationToken cancellationToken)
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
            return string.IsNullOrWhiteSpace(id)
                ? (null, false, "")
                : (id, (inspected?["RepoDigests"]?.AsArray() ?? []).Count == 0 && !ImageReference.IsPatched(image), ((string?)inspected?["Config"]?["User"] ?? "").Trim());
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException or JsonException)
        {
            return (null, false, "");
        }
    }

    /// <summary>
    /// Each Python library with a fix published, at the version that fixes all
    /// of its vulnerabilities (the highest of their fixes): <c>aiohttp&gt;=3.13.6</c>.
    /// A library one of whose vulnerabilities has no fix yet is still raised as
    /// far as the others go.
    /// </summary>
    internal static List<string> PythonFixes(IEnumerable<ImageVulnerability> found) =>
        [.. found.Where(v => v.Ecosystem == Installed.PyPI && v.FixedIn.Length > 0)
            .GroupBy(v => v.Package, StringComparer.OrdinalIgnoreCase)
            .OrderBy(library => library.Key, StringComparer.OrdinalIgnoreCase)
            .Select(library => $"{library.Key}>={library.Select(v => v.FixedIn).Order(VersionOrder.Instance).Last()}")];

    /// <summary>
    /// Every library line of the image's requirements files, as it is; a
    /// vulnerable one with the line Zaindari suggests (the library at the
    /// version that fixes all of its own), or, when no version fixes one of
    /// them, that it is to be replaced or taken out.
    /// </summary>
    internal static List<ImageRequirement> Requirements(IEnumerable<Installed> files, IReadOnlyList<ImageVulnerability> found)
    {
        var vulnerable = found.Where(v => v.Ecosystem == Installed.PyPI)
            .GroupBy(v => Normalized(v.Package))
            .ToDictionary(library => library.Key, library => library.ToList());
        var said = new List<ImageRequirement>();
        foreach (var file in files)
        {
            foreach (var raw in file.Version.Split('\n'))
            {
                var line = raw.Split('#')[0].Trim();
                if (RequirementName().Match(line) is not { Success: true } named)
                {
                    continue;
                }

                var name = named.Groups["name"].Value + named.Groups["extras"].Value;
                if (!vulnerable.TryGetValue(Normalized(named.Groups["name"].Value), out var library))
                {
                    said.Add(new ImageRequirement(file.Name, line, "", "", name));
                    continue;
                }

                var fixes = library.Where(v => v.FixedIn.Length > 0).Select(v => v.FixedIn).ToList();
                var unfixed = library.Count(v => v.FixedIn.Length == 0);
                var fixedAll = fixes.Count > 0 ? fixes.Order(VersionOrder.Instance).Last() : "";
                said.Add(new ImageRequirement(file.Name, line,
                    fixedAll.Length > 0 ? $"{named.Groups["name"].Value}{named.Groups["extras"].Value}>={fixedAll}" : "",
                    unfixed == 0 ? $"{fixedAll} fixes {(library.Count == 1 ? "it" : $"all {library.Count}")}"
                    : fixedAll.Length > 0 ? $"{fixedAll} fixes {fixes.Count}; no version fixes {unfixed} yet: replace it, or take it out"
                    : "no version fixes it yet: replace it, or take it out", name, Vulnerable: true));
            }
        }

        return said;
    }

    /// <summary>The library a requirements line names, and its extras (<c>uvicorn[standard]</c>); an option (<c>-r</c>, <c>--index-url</c>) or a URL names none.</summary>
    [GeneratedRegex(@"^(?<name>[A-Za-z0-9][A-Za-z0-9._-]*)(?<extras>\[[^\]]*\])?")]
    private static partial Regex RequirementName();

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
        // A system package is not in the requirements: it comes with the base image, or an upgrade in the Dockerfile.
        var how = first.Ecosystem == Installed.PyPI ? "" : ": build on a newer base image, or upgrade it in the Dockerfile";
        return $"{image}: {first.Named} {first.Version}: {counted} moderate or worse{ImageRemedies.WorstSaid}{worst}; {remedy}{how}.";
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

    /// <summary>
    /// What Zaindari's window shows: every package of the image and every known
    /// vulnerability of them, the low ones too, the worst first, with the lines
    /// a refusal would give and the patch it can be given
    /// (<see cref="ImagePatches"/>). Nothing is refused by asking, whatever
    /// Settings → Security says; what is found marks the image.
    /// </summary>
    public async Task<ImageVulnerabilities> ReportAsync(string image, CancellationToken cancellationToken = default)
    {
        image = image.Trim();
        var (id, built, user) = await OriginAsync(image, cancellationToken);
        if (id is null)
        {
            return new ImageVulnerabilities(image, false, false, false, false, false, [], [], [], $"{image} is not on this machine.");
        }

        List<Installed>? packages;
        List<Installed> files;
        bool ubuntuPro;
        try
        {
            (packages, files, ubuntuPro) = Split(await PackagesAsync(image, id, cancellationToken));
        }
        catch (Exception ex) when (ex is TimeoutException or WslcNotFoundException or WslcException or IOException or InvalidDataException)
        {
            return new ImageVulnerabilities(image, true, built, false, false, false, [], [], [], $"{image} could not be read ({ex.Message}).");
        }

        if (packages is null)
        {
            Mark(id, image, built, []);
            return new ImageVulnerabilities(image, true, built, false, true, false, [], [], [], $"{image} has no package to check: no Python library, and no package database of a distribution OSV.dev knows (Alpine's apk, Debian's or Ubuntu's dpkg). That is not a clean image.");
        }

        var listed = packages.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(p => new ImagePackage(p.Name, p.Version, p.Ecosystem)).ToList();
        List<ImageVulnerability> found;
        try
        {
            found = Reachable(await OsvAsync(packages, cancellationToken), ubuntuPro);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new ImageVulnerabilities(image, true, built, true, false, false, listed, [], [], $"OSV.dev could not be reached ({ex.Message}): the libraries are listed unchecked.");
        }

        Mark(id, image, built, found);
        var remedies = found.Where(v => v.Blocks).GroupBy(v => (v.Ecosystem, v.Package, v.Version)).Select(library => Remedy(image, [.. library])).ToList();
        var fixes = PythonFixes(found);
        var requirements = Requirements(files, found);
        var advisories = found
            .OrderByDescending(v => Array.IndexOf(SeverityOrder, v.Severity))
            .ThenBy(v => v.Package, StringComparer.OrdinalIgnoreCase)
            .Select(v => v.ToAdvisory())
            .ToList();
        return new ImageVulnerabilities(image, true, built, true, true, built && remedies.Count > 0 && settings.Get().Policy == ImageSecurityPolicy.Block,
            listed, advisories, remedies, Patch: ImagePatches.Named(image),
            SystemUpgrade: ImagePatches.SystemUpgrade(packages, found), PythonFixes: fixes, PythonInstall: ImagePatches.PythonInstall(packages, fixes),
            Requirements: requirements, PythonInstaller: ImagePatches.Installer(packages), PythonUninstaller: ImagePatches.Uninstaller(packages),
            PatchDockerfile: ImagePatches.Dockerfile(image, ImagePatches.SystemUpgrade(packages, found), [], user),
            RequirementFiles: [.. files.Select(file => new ImageTextFile(file.Name, file.Version))]);
    }

    /// <summary>The image's packages, from what was read under its id already, or read now and kept; null for an image with none to read. The kernel is left out (<see cref="WithoutKernel"/>).</summary>
    private async Task<List<Installed>?> PackagesAsync(string image, string? id, CancellationToken cancellationToken)
    {
        if (id is not null && (_packages.TryGetValue(id, out var kept) || Kept(id, out kept)) && !ReadByEarlierRule(kept))
        {
            _packages[id] = kept;
            return WithoutKernel(kept);
        }

        List<Installed>? packages = await InstalledPackagesAsync(image, cancellationToken) is { } read ? [.. read, new Installed(Installed.ReadBy, ReadRule, "")] : null;
        if (id is not null)
        {
            _packages[id] = packages;
            Keep(id, packages);
        }

        return WithoutKernel(packages);
    }

    /// <summary>
    /// The rule an image's packages are read by now: 1 when whether an Ubuntu
    /// image is attached to Ubuntu Pro came to be read, and a Python library
    /// Debian or Ubuntu installed to be left to its package.
    /// </summary>
    internal const string ReadRule = "1";

    /// <summary>Packages kept by an earlier rule than <see cref="ReadRule"/>: read again, once.</summary>
    private static bool ReadByEarlierRule(List<Installed>? kept) =>
        kept is not null && !kept.Any(package => package.Ecosystem == Installed.ReadBy && package.Name == ReadRule);

    /// <summary>
    /// The packages without Debian's and Ubuntu's kernel (source package
    /// <c>linux</c>: its headers, <c>linux-libc-dev</c>, are what an image
    /// carries of it). A container runs on the kernel of WSL's virtual machine,
    /// never on its own image's, so the kernel's vulnerabilities, hundreds of
    /// them, are not the container's.
    /// </summary>
    internal static List<Installed>? WithoutKernel(List<Installed>? packages) =>
        packages?.Where(package => !(package.Name == "linux"
            && (package.Ecosystem.StartsWith("Debian:", StringComparison.Ordinal) || package.Ecosystem.StartsWith("Ubuntu:", StringComparison.Ordinal)))).ToList();

    /// <summary>What an image's file in <c>keptIn</c> says: its libraries, null for none; false when it has no file, or one that cannot be read.</summary>
    private bool Kept(string id, out List<Installed>? packages)
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

            packages = kept.Packages?.Select(p => new Installed(p.Ecosystem, p.Name, p.Version)).ToList();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning("image security check: {Path} could not be read, the image is read again: {Message}", path, ex.Message);
            return false;
        }
    }

    private void Keep(string id, List<Installed>? packages)
    {
        if (KeptPath(id) is not { } path)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new KeptPackages(packages?.Select(p => new KeptPackage(p.Ecosystem, p.Name, p.Version)).ToList()), KeptJson));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("image security check: {Path} could not be written, the image will be read again after a restart: {Message}", path, ex.Message);
        }
    }

    /// <summary>
    /// The image's file in <c>keptIn</c>, named by its id (<c>sha256-…</c>) and the
    /// reading's kind (<c>.packages</c>: libraries and system packages; an older
    /// file, of libraries alone, is not read, and the image is read once more);
    /// null when nothing is kept on disk.
    /// </summary>
    // Named again when what is kept grew (the requirements files): an image read before is read once more.
    private string? KeptPath(string id) =>
        keptIn is null ? null : Path.Combine(keptIn, $"{id.Replace(':', '-')}.packages-requirements.json");

    /// <summary>A <c>requirements*.txt</c> of the image's own, not one a library ships in its site-packages or a node module.</summary>
    private static bool IsRequirementsFile(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        return name.StartsWith("requirements", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("site-packages/", StringComparison.Ordinal) && !path.Contains("dist-packages/", StringComparison.Ordinal)
            && !path.Contains("node_modules/", StringComparison.Ordinal);
    }

    /// <summary>A requirements file larger than this is not one a person wrote.</summary>
    private const long MaxRequirementsFile = 256 * 1024;

    /// <summary>The packages apart from the requirements files kept with them, and whether the image is attached to Ubuntu Pro; the packages null when there is none.</summary>
    private static (List<Installed>? Packages, List<Installed> Files, bool UbuntuPro) Split(List<Installed>? read)
    {
        var files = (read ?? []).Where(entry => entry.Ecosystem == Installed.RequirementsFile).ToList();
        var packages = (read ?? []).Where(entry => entry.Ecosystem is not Installed.RequirementsFile and not Installed.UbuntuPro and not Installed.ReadBy).ToList();
        var ubuntuPro = (read ?? []).Any(entry => entry.Ecosystem == Installed.UbuntuPro && entry.Name == Installed.Attached);
        return (read is null || packages.Count == 0 ? null : packages, files, ubuntuPro);
    }

    /// <summary>
    /// The fixes an image's own upgrade reaches: in an image attached to Ubuntu
    /// Pro (its ESM sources and their credentials left in it), a fix only Pro
    /// carries is one like any other, which a patch's upgrade installs; in any
    /// other, it stays said and nothing more.
    /// </summary>
    internal static List<ImageVulnerability> Reachable(List<ImageVulnerability> found, bool ubuntuPro) =>
        !ubuntuPro ? found
        : [.. found.Select(v => v.UbuntuProFixedIn.Length > 0 ? v with { FixedIn = v.UbuntuProFixedIn, UbuntuProFixedIn = "" } : v)];

    /// <summary>
    /// A vulnerability something can be done about: a fix the image's upgrade
    /// reaches, or a Python library, which can be raised, replaced or taken
    /// out. A system package with no fix its distribution gives the image is
    /// its distribution's to mend: it is said, not raised as an alarm, since
    /// nobody downstream can mend it.
    /// </summary>
    internal static bool Mendable(ImageVulnerability v) => v.FixedIn.Length > 0 || v.Ecosystem == Installed.PyPI;

    private static readonly JsonSerializerOptions KeptJson = new(JsonSerializerDefaults.Web);

    /// <summary>An image's packages as kept on disk; null for an image with none to read.</summary>
    private sealed record KeptPackages(List<KeptPackage>? Packages);

    private sealed record KeptPackage(string Ecosystem, string Name, string Version);

    /// <summary>
    /// What the check found, kept on the image (<see cref="ImageMarks"/>) by its
    /// id and the name it was checked by: the worst of its vulnerabilities
    /// moderate or worse that something can be done about (<see cref="Mendable"/>),
    /// the worst of those nobody downstream can mend, and whether the agent
    /// refuses it; none when it is clean.
    /// </summary>
    private void Mark(string? id, string image, bool built, IReadOnlyList<ImageVulnerability> found)
    {
        if (marks is null || id is null)
        {
            return;
        }

        string Worst(Func<ImageVulnerability, bool> which) =>
            found.Where(v => v.Blocks && which(v)).Select(v => v.Severity).MaxBy(severity => Array.IndexOf(SeverityOrder, severity)) ?? "";
        marks.Record(id, image, Worst(Mendable), Worst(v => !Mendable(v)),
            built && found.Any(v => v.Blocks) && settings.Get().Policy == ImageSecurityPolicy.Block);
    }

    private async Task<List<ImageVulnerability>> CheckAsync(string image, string? id, bool built, Action<string, string> note, CancellationToken cancellationToken)
    {
        List<Installed>? packages;
        bool ubuntuPro;
        try
        {
            (packages, _, ubuntuPro) = Split(await PackagesAsync(image, id, cancellationToken));
        }
        catch (Exception ex) when (ex is TimeoutException or WslcNotFoundException or WslcException or IOException or InvalidDataException)
        {
            note("WARNING", $"{image} could not be read ({ex.Message}): not checked.");
            return [];
        }

        if (packages is null)
        {
            Mark(id, image, built, []);
            note("INFO", $"{image} has no package to check: no Python library, and no package database of a distribution OSV.dev knows (Alpine's apk, Debian's or Ubuntu's dpkg). That is not a clean image.");
            return [];
        }

        try
        {
            var found = Reachable(await OsvAsync(packages, cancellationToken), ubuntuPro);
            if (found.Count == 0)
            {
                note("INFO", $"{image}: {packages.Count} packages, no known vulnerability.");
            }

            Mark(id, image, built, found);
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
    /// The image's packages, read from its files and never by running it:
    /// anything run in an image runs its code (a Python reads its own
    /// sitecustomize and .pth files as it starts), and a scanner that runs what
    /// it scans is a way in. A container of it is made and never started, its
    /// files exported to a temporary archive, and what is installed read from it
    /// (<see cref="InstalledAsync"/>): every Python library, a virtual
    /// environment's too, and the distribution's packages; then both are let
    /// go. Null when it has no package to read.
    /// </summary>
    private async Task<List<Installed>?> InstalledPackagesAsync(string image, CancellationToken cancellationToken)
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
    /// Every installed package an exported filesystem holds, each once: its
    /// Python libraries, by their metadata (a <c>Name:</c> and a
    /// <c>Version:</c> among the headers each library's METADATA or PKG-INFO
    /// begins with, in a site-packages or dist-packages folder), and its
    /// distribution's packages, by the distribution's own database
    /// (<c>lib/apk/db/installed</c>, <c>var/lib/dpkg/status</c> and a distroless
    /// image's <c>status.d</c>), in the ecosystem its <c>os-release</c> names.
    /// Null when there is none. An installer may keep a library's metadata
    /// elsewhere and link it into site-packages (uv links from its cache): a
    /// hard link in the archive carries no data, only the path it is to, so
    /// every metadata file is kept as it comes, and a link is read through the
    /// one it names. A distribution OSV.dev has no name for (Kali, say) gives
    /// no system package: it is not checked, and never reads as clean.
    /// </summary>
    internal static async Task<List<Installed>?> InstalledAsync(string archive, CancellationToken cancellationToken)
    {
        var found = new HashSet<Installed>();
        var seen = new Dictionary<string, Installed?>(StringComparer.Ordinal);
        var system = new List<(string Name, string Version)>();
        var release = new Dictionary<string, string>(StringComparer.Ordinal);
        var (proSources, proCredentials) = (false, false);
        await using var file = File.OpenRead(archive);
        await using var reader = new TarReader(file);
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
        {
            var path = Normalized(entry.Name);
            proSources |= IsUbuntuProSource(path);
            proCredentials |= path == UbuntuProCredentials;
            if (entry.DataStream is { } content && entry.EntryType != TarEntryType.HardLink)
            {
                if (path is "etc/os-release" or "usr/lib/os-release")
                {
                    foreach (var (key, value) in await OsReleaseAsync(content, cancellationToken))
                    {
                        release.TryAdd(key, value);
                    }

                    continue;
                }

                if (path == "lib/apk/db/installed")
                {
                    system.AddRange(await ApkAsync(content, cancellationToken));
                    continue;
                }

                if (path == "var/lib/dpkg/status" || (path.StartsWith("var/lib/dpkg/status.d/", StringComparison.Ordinal) && !path.EndsWith(".md5sums", StringComparison.Ordinal)))
                {
                    system.AddRange(await DpkgAsync(content, cancellationToken));
                    continue;
                }
            }

            if (IsRequirementsFile(path) && entry.DataStream is { } requirements && entry.EntryType != TarEntryType.HardLink && entry.Length < MaxRequirementsFile)
            {
                using var text = new StreamReader(requirements);
                found.Add(new Installed(Installed.RequirementsFile, "/" + path, await text.ReadToEndAsync(cancellationToken)));
                continue;
            }

            if (!IsMetadataFile(entry.Name))
            {
                continue;
            }

            var library = entry.EntryType == TarEntryType.HardLink
                ? seen.GetValueOrDefault(Normalized(entry.LinkName))
                : entry.DataStream is { } data ? await HeadersAsync(data, cancellationToken) : null;
            seen[path] = library;
            if (library is { } installed && IsInstalled(path))
            {
                found.Add(installed);
            }
        }

        if (Ecosystem(release) is { } ecosystem)
        {
            found.UnionWith(system.Select(package => new Installed(ecosystem, package.Name, package.Version)));
            if (ecosystem.StartsWith("Ubuntu:", StringComparison.Ordinal))
            {
                found.Add(new Installed(Installed.UbuntuPro, proSources && proCredentials ? Installed.Attached : Installed.Free, ""));
            }
        }

        return found.Count == 0 ? null : [.. found.OrderBy(package => package.Ecosystem, StringComparer.Ordinal).ThenBy(package => package.Name, StringComparer.OrdinalIgnoreCase)];

        // An archive names a link's target as it names its entries, with or without a leading ./ or /.
        static string Normalized(string path) => path.TrimStart('.').TrimStart('/');
    }

    /// <summary>
    /// An Ubuntu Pro ESM source, as its client writes it on attaching
    /// (<c>ubuntu-esm-infra</c>, <c>ubuntu-esm-apps</c>; <c>.sources</c> from
    /// 24.04, <c>.list</c> before), and takes away again on detaching, which is
    /// what Canonical's Dockerfile does at the end of its build.
    /// </summary>
    private static bool IsUbuntuProSource(string path) =>
        path.StartsWith("etc/apt/sources.list.d/ubuntu-esm-", StringComparison.Ordinal)
        && (path.EndsWith(".sources", StringComparison.Ordinal) || path.EndsWith(".list", StringComparison.Ordinal));

    /// <summary>The credentials the ESM sources are read with, without which an upgrade cannot reach them.</summary>
    private const string UbuntuProCredentials = "etc/apt/auth.conf.d/90ubuntu-advantage";

    /// <summary>
    /// The distribution's ecosystem in OSV.dev's names, from its <c>os-release</c>:
    /// <c>Alpine:v3.20</c> (the major and minor of its version), <c>Debian:12</c>,
    /// <c>Ubuntu:22.04</c>; null for one OSV.dev does not know, or no version (a
    /// rolling one).
    /// </summary>
    internal static string? Ecosystem(IReadOnlyDictionary<string, string> release)
    {
        var id = release.GetValueOrDefault("ID", "");
        var version = release.GetValueOrDefault("VERSION_ID", "");
        if (version.Length == 0)
        {
            return null;
        }

        return id switch
        {
            "alpine" when Regex.Match(version, @"^\d+\.\d+") is { Success: true } minor => $"Alpine:v{minor.Value}",
            "debian" when Regex.Match(version, @"^\d+") is { Success: true } major => $"Debian:{major.Value}",
            "ubuntu" when Regex.Match(version, @"^\d+\.\d+") is { Success: true } numbered => $"Ubuntu:{numbered.Value}",
            _ => null,
        };
    }

    /// <summary>An <c>os-release</c> file's <c>KEY=value</c> lines, the value's quotes taken off.</summary>
    private static async Task<List<(string Key, string Value)>> OsReleaseAsync(Stream data, CancellationToken cancellationToken)
    {
        var pairs = new List<(string, string)>();
        using var text = new StreamReader(data);
        while (await text.ReadLineAsync(cancellationToken) is { } line)
        {
            var at = line.IndexOf('=');
            if (at > 0)
            {
                pairs.Add((line[..at].Trim(), line[(at + 1)..].Trim().Trim('"', '\'')));
            }
        }

        return pairs;
    }

    /// <summary>Alpine's installed packages, a block each: <c>P:</c> the package, <c>o:</c> its origin, which OSV.dev names it by, <c>V:</c> its version.</summary>
    private static async Task<List<(string Name, string Version)>> ApkAsync(Stream data, CancellationToken cancellationToken)
    {
        var packages = new List<(string, string)>();
        string? name = null, origin = null, version = null;
        using var text = new StreamReader(data);
        while (true)
        {
            var line = await text.ReadLineAsync(cancellationToken);
            if (line is null || line.Length == 0)
            {
                if ((origin ?? name) is { Length: > 0 } named && version is { Length: > 0 })
                {
                    packages.Add((named, version));
                }

                (name, origin, version) = (null, null, null);
                if (line is null)
                {
                    return packages;
                }

                continue;
            }

            if (line.StartsWith("P:", StringComparison.Ordinal))
            {
                name = line[2..].Trim();
            }
            else if (line.StartsWith("o:", StringComparison.Ordinal))
            {
                origin = line[2..].Trim();
            }
            else if (line.StartsWith("V:", StringComparison.Ordinal))
            {
                version = line[2..].Trim();
            }
        }
    }

    /// <summary>
    /// Debian's or Ubuntu's installed packages, a paragraph each, named as OSV.dev
    /// names them: by their <c>Source:</c> package, at the source's version when
    /// it gives one (<c>Source: openssl (3.0.11-1)</c>), else by the package
    /// itself; only one whose <c>Status:</c> says installed.
    /// </summary>
    private static async Task<List<(string Name, string Version)>> DpkgAsync(Stream data, CancellationToken cancellationToken)
    {
        var packages = new List<(string, string)>();
        string? name = null, source = null, version = null, status = null;
        using var text = new StreamReader(data);
        while (true)
        {
            var line = await text.ReadLineAsync(cancellationToken);
            if (line is null || line.Length == 0)
            {
                if (name is { Length: > 0 } && version is { Length: > 0 } && (status is null || status.EndsWith(" installed", StringComparison.Ordinal)))
                {
                    var from = source is { Length: > 0 } ? Regex.Match(source, @"^(?<name>\S+)(\s+\((?<version>[^)]+)\))?") : null;
                    packages.Add(from is { Success: true }
                        ? (from.Groups["name"].Value, from.Groups["version"].Success ? from.Groups["version"].Value : version)
                        : (name, version));
                }

                (name, source, version, status) = (null, null, null, null);
                if (line is null)
                {
                    return packages;
                }

                continue;
            }

            if (line.StartsWith("Package:", StringComparison.Ordinal))
            {
                name = line[8..].Trim();
            }
            else if (line.StartsWith("Source:", StringComparison.Ordinal))
            {
                source = line[7..].Trim();
            }
            else if (line.StartsWith("Version:", StringComparison.Ordinal))
            {
                version = line[8..].Trim();
            }
            else if (line.StartsWith("Status:", StringComparison.Ordinal))
            {
                status = line[7..].Trim();
            }
        }
    }

    /// <summary>The <c>Name:</c> and <c>Version:</c> of a metadata file; the headers end at the first empty line, and the description after it is not read.</summary>
    private static async Task<Installed?> HeadersAsync(Stream data, CancellationToken cancellationToken)
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

        return name is { Length: > 0 } && version is { Length: > 0 } ? new Installed(Installed.PyPI, name, version) : null;
    }

    /// <summary>A library's metadata wherever it is: <c>x-1.0.dist-info/METADATA</c>, <c>x.egg-info/PKG-INFO</c>, or an <c>x.egg-info</c> file.</summary>
    private static bool IsMetadataFile(string path) =>
        path.EndsWith(".dist-info/METADATA", StringComparison.Ordinal)
        || path.EndsWith(".egg-info/PKG-INFO", StringComparison.Ordinal)
        || path.EndsWith(".egg-info", StringComparison.Ordinal);

    /// <summary>
    /// Installed where Python imports from, by pip or uv: a site-packages or
    /// dist-packages folder, not an installer's cache. Not Debian's and
    /// Ubuntu's own <c>usr/lib/python3/dist-packages</c>, where their packages
    /// (<c>python3-flask</c>) put a library: it is the distribution's, checked
    /// as its package is, by its tracker, since it mends it without changing
    /// the version PyPI would read, and no pip installs over it.
    /// </summary>
    private static bool IsInstalled(string path) =>
        path.Contains("-packages/", StringComparison.Ordinal) && !path.StartsWith(DistributionPython, StringComparison.Ordinal);

    /// <summary>Where Debian's and Ubuntu's packages put their Python libraries.</summary>
    private const string DistributionPython = "usr/lib/python3/dist-packages/";

    /// <summary>OSV.dev's known vulnerabilities of these packages, each in its own ecosystem, one batch question, each advisory read once.</summary>
    private async Task<List<ImageVulnerability>> OsvAsync(List<Installed> packages, CancellationToken cancellationToken)
    {
        var found = new List<ImageVulnerability>();
        foreach (var batch in packages.Chunk(500))
        {
            using var response = await http.PostAsJsonAsync("https://api.osv.dev/v1/querybatch", new
            {
                queries = batch.Select(p => new { package = new { name = p.Name, ecosystem = p.Ecosystem }, version = p.Version }),
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
                var advisories = ids.Select(id => (Id: id, Advisory: read[id])).Where(a => !Unimportant(a.Advisory, batch[i])).ToList();

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

    /// <summary>
    /// An advisory the package's own distribution says is not a security
    /// problem for it: Debian's <c>urgency: unimportant</c> on its release,
    /// Ubuntu's priority <c>negligible</c>. The distribution will not fix it,
    /// nobody downstream can, and counting it would leave its images red for
    /// good. It is left out, as the distribution leaves it.
    /// </summary>
    internal static bool Unimportant(JsonNode? advisory, Installed package) =>
        package.Ecosystem.StartsWith("Debian:", StringComparison.Ordinal)
            ? OwnRelease(advisory, package).Any(affected => (string?)affected?["ecosystem_specific"]?["urgency"] == "unimportant")
            : package.Ecosystem.StartsWith("Ubuntu:", StringComparison.Ordinal) && UbuntuPriority(advisory) == "negligible";

    private async Task<JsonNode?> AdvisoryAsync(string id, CancellationToken cancellationToken)
    {
        if ((_advisories.TryGetValue(id, out var kept) || KeptAdvisory(id, out kept)) && DateTimeOffset.UtcNow - kept.Read < AdvisoryKept)
        {
            _advisories[id] = kept;
            return kept.Advisory;
        }

        var text = await http.GetStringAsync($"https://api.osv.dev/v1/vulns/{id}", cancellationToken);
        var advisory = JsonNode.Parse(text);
        _advisories[id] = (advisory, DateTimeOffset.UtcNow);
        KeepAdvisory(id, text);
        return advisory;
    }

    /// <summary>An advisory as kept on disk, with when it was read: the file's own time.</summary>
    private bool KeptAdvisory(string id, out (JsonNode? Advisory, DateTimeOffset Read) kept)
    {
        kept = default;
        if (AdvisoryPath(id) is not { } path || !File.Exists(path))
        {
            return false;
        }

        try
        {
            kept = (JsonNode.Parse(File.ReadAllText(path)), new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning("image security check: {Path} could not be read, the advisory is asked again: {Message}", path, ex.Message);
            return false;
        }
    }

    private void KeepAdvisory(string id, string text)
    {
        if (AdvisoryPath(id) is not { } path)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("image security check: {Path} could not be written, the advisory will be asked again after a restart: {Message}", path, ex.Message);
        }
    }

    /// <summary>The advisory's file in <c>advisoriesIn</c>, named by its id (<c>GHSA-…</c>, <c>PYSEC-…</c>); null when none is kept on disk.</summary>
    private string? AdvisoryPath(string id) =>
        advisoriesIn is null || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? null : Path.Combine(advisoriesIn, id + ".json");

    private static IEnumerable<string> Aliases(JsonNode? advisory) =>
        (advisory?["aliases"]?.AsArray() ?? []).Select(a => (string?)a).OfType<string>();

    /// <summary>
    /// One advisory of one library. An advisory that gives no severity (a PYSEC
    /// one, whose GHSA twin OSV.dev did not list) takes its GHSA alias's, or
    /// else the one its CVSS vector scores: left unknown, a critical one would
    /// not stop the build.
    /// </summary>
    private async Task<ImageVulnerability> ReadAsync(string id, JsonNode? advisory, Installed package, CancellationToken cancellationToken)
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
            .Where(s => (string?)s?["type"] is "CVSS_V3" or "CVSS_V4")
            .Select(s => (string?)s?["score"]).OfType<string>();
        return read with { Severity = vectors.Select(CvssSeverity).FirstOrDefault(s => s != Unknown) ?? Unknown };
    }

    private static ImageVulnerability Read(string id, JsonNode? advisory, Installed package)
    {
        var aliases = Aliases(advisory).Concat((advisory?["upstream"]?.AsArray() ?? []).Select(a => (string?)a).OfType<string>()).ToList();
        var fixedIn = (advisory?["affected"]?.AsArray() ?? [])
            .Where(a => package.Ecosystem == Installed.PyPI
                ? Normalized((string?)a?["package"]?["name"]) == Normalized(package.Name)
                : (string?)a?["package"]?["name"] == package.Name && ((string?)a?["package"]?["ecosystem"] ?? "").StartsWith(package.Ecosystem, StringComparison.Ordinal))
            .SelectMany(a => a?["ranges"]?.AsArray() ?? [])
            .SelectMany(r => r?["events"]?.AsArray() ?? [])
            .Select(e => (string?)e?["fixed"]).OfType<string>()
            .FirstOrDefault() ?? "";
        var severity = DistributionSeverity(advisory, package) is var rated && rated != Unknown ? rated : Severity(advisory);
        return new ImageVulnerability(package.Name, package.Version, id, aliases.FirstOrDefault(a => a.StartsWith("CVE-", StringComparison.Ordinal)) ?? "",
            severity, fixedIn, $"https://osv.dev/vulnerability/{id}", package.Ecosystem, fixedIn.Length == 0 ? UbuntuProFix(advisory, package) : "");
    }

    /// <summary>The severity the advisory's database gives it (GitHub's: low, moderate or medium, high, critical).</summary>
    private static string Severity(JsonNode? advisory) => Rated((string?)advisory?["database_specific"]?["severity"]);

    /// <summary>A rating in Zaindari's words: <c>medium</c> is <c>moderate</c>; anything else that is not one of the four is unknown.</summary>
    private static string Rated(string? rating) => (rating ?? "").Trim().ToLowerInvariant() switch
    {
        "medium" => "moderate",
        var known when known is "low" or "moderate" or "high" or "critical" => known,
        _ => Unknown,
    };

    /// <summary>
    /// The severity the package's own distribution gives the advisory, which
    /// rules over NVD's CVSS score: it is the distribution that decides whether
    /// and when a fix comes, and a CVSS critical it rates medium would leave
    /// its images red with nothing anyone can do. Ubuntu's priority
    /// (<c>severity</c> of type <c>Ubuntu</c>), Debian's urgency on its own
    /// release (<c>ecosystem_specific.urgency</c>, mostly "not yet assigned",
    /// which is no rating); Alpine gives none. Unknown when it gives none.
    /// </summary>
    internal static string DistributionSeverity(JsonNode? advisory, Installed package)
    {
        if (package.Ecosystem.StartsWith("Ubuntu:", StringComparison.Ordinal))
        {
            return Rated(UbuntuPriority(advisory));
        }

        return package.Ecosystem.StartsWith("Debian:", StringComparison.Ordinal)
            ? Rated(OwnRelease(advisory, package).Select(affected => (string?)affected?["ecosystem_specific"]?["urgency"]).FirstOrDefault(urgency => urgency is not null))
            : Unknown;
    }

    /// <summary>Ubuntu's priority of an advisory: <c>negligible</c>, <c>low</c>, <c>medium</c>, <c>high</c> or <c>critical</c>; null when it gives none.</summary>
    private static string? UbuntuPriority(JsonNode? advisory) =>
        (advisory?["severity"]?.AsArray() ?? []).Where(s => (string?)s?["type"] == "Ubuntu").Select(s => (string?)s?["score"]).FirstOrDefault();

    /// <summary>The advisory's entries for this very package on this very release of its distribution.</summary>
    private static IEnumerable<JsonNode?> OwnRelease(JsonNode? advisory, Installed package) =>
        (advisory?["affected"]?.AsArray() ?? []).Where(affected =>
            (string?)affected?["package"]?["name"] == package.Name
            && (string?)affected?["package"]?["ecosystem"] == package.Ecosystem);

    /// <summary>
    /// The version Ubuntu Pro, Canonical's subscription, fixes the package in
    /// on this release, when the free archive does not (<c>Ubuntu:Pro:24.04:LTS</c>
    /// beside <c>Ubuntu:24.04:LTS</c>): an upgrade cannot reach it, so it is
    /// not a fix a patch installs, only said. Empty when it has none.
    /// </summary>
    internal static string UbuntuProFix(JsonNode? advisory, Installed package) =>
        !package.Ecosystem.StartsWith("Ubuntu:", StringComparison.Ordinal) ? ""
        : (advisory?["affected"]?.AsArray() ?? [])
            .Where(a => (string?)a?["package"]?["name"] == package.Name
                && ((string?)a?["package"]?["ecosystem"] ?? "").StartsWith("Ubuntu:Pro:" + package.Ecosystem["Ubuntu:".Length..], StringComparison.Ordinal))
            .SelectMany(a => a?["ranges"]?.AsArray() ?? [])
            .SelectMany(r => r?["events"]?.AsArray() ?? [])
            .Select(e => (string?)e?["fixed"]).OfType<string>()
            .FirstOrDefault() ?? "";

    /// <summary>
    /// The severity a CVSS vector scores, by its base score as the
    /// specification computes it: a 3.x one by FIRST's CVSS v3.1, section 7,
    /// a 4.0 one by <see cref="Cvss4"/>. Under 4 low, under 7 moderate, under
    /// 9 high, else critical; a vector that cannot be read, or that scores 0,
    /// is unknown.
    /// </summary>
    internal static string CvssSeverity(string vector)
    {
        if (vector.StartsWith("CVSS:4.0/", StringComparison.Ordinal))
        {
            return Cvss4.Score(vector) is double scored and > 0 ? Band(scored) : Unknown;
        }

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
        return Band(RoundUp(Math.Min((changed ? 1.08 : 1) * (impact + exploitability), 10)));
    }

    /// <summary>The severity of a CVSS base score above 0, by the bands both versions share.</summary>
    private static string Band(double score) =>
        score switch { < 4 => "low", < 7 => "moderate", < 9 => "high", _ => "critical" };

    /// <summary>The specification's Roundup: up to one decimal, read through whole numbers so 4.0000001 is not 4.1.</summary>
    private static double RoundUp(double value)
    {
        var whole = (long)Math.Round(value * 100000);
        return whole % 10000 == 0 ? whole / 100000.0 : (Math.Floor(whole / 10000.0) + 1) / 10.0;
    }

    /// <summary>PyPI's names compare as PEP 503 says: case and <c>_</c>, <c>-</c>, <c>.</c> alike.</summary>
    private static string Normalized(string? name) => (name ?? "").ToLowerInvariant().Replace('_', '-').Replace('.', '-');
}
