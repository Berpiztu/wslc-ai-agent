namespace WslcAgent.ApiClient.Contracts;

/// <summary>What the agent does once it has built an image (<see cref="ImageSecuritySettings.Policy"/>).</summary>
public static class ImageSecurityPolicy
{
    /// <summary>Built images are not checked.</summary>
    public const string Off = "off";

    /// <summary>Built images are checked, and what is found is said in the build's log; nothing stops.</summary>
    public const string Warn = "warn";

    /// <summary>Built images are checked, and one with a vulnerability moderate or worse starts no container: the build fails.</summary>
    public const string Block = "block";

    public static bool IsKnown(string policy) => policy is Off or Warn or Block;
}

/// <summary>
/// Body of <c>GET</c> and <c>PUT /api/v1/image-security</c>: Settings → Security →
/// Image security check. Each image the agent builds, a Compose project's or
/// one from Build image, has its Python libraries read (<c>pip list</c> in the
/// image itself) and asked of OSV.dev for known vulnerabilities, before any
/// container is made from it.
/// </summary>
/// <param name="Policy"><see cref="ImageSecurityPolicy.Off"/>, <see cref="ImageSecurityPolicy.Warn"/>, or <see cref="ImageSecurityPolicy.Block"/>, the default.</param>
public sealed record ImageSecuritySettings(string Policy = ImageSecurityPolicy.Block);

/// <summary>A package an image carries, read in the image's own files: a Python library (<c>PyPI</c>), or a system package of its distribution (<c>Alpine:v3.20</c>, <c>Debian:12</c>, <c>Ubuntu:22.04</c>).</summary>
public sealed record ImagePackage(string Name, string Version, string Ecosystem = "PyPI");

/// <summary>A known vulnerability of one package of an image, as OSV.dev gives it.</summary>
/// <param name="Ecosystem">The package's kind, as <see cref="ImagePackage.Ecosystem"/>: <c>PyPI</c> for a Python library, the distribution's for a system package.</param>
/// <param name="Severity"><c>low</c>, <c>moderate</c>, <c>high</c>, <c>critical</c>, or <c>unknown</c> when neither the advisory, its GHSA alias nor its CVSS vector gives one.</param>
/// <param name="FixedIn">The first version that fixes it; empty when none does yet.</param>
/// <param name="Url">The advisory on osv.dev.</param>
/// <param name="UbuntuProFixedIn">When <paramref name="FixedIn"/> is empty, the version only Ubuntu Pro, Canonical's subscription, fixes it in: an upgrade cannot reach it, so no patch installs it. Empty otherwise.</param>
/// <param name="Accepted">A person accepted it for this image (<see cref="ImageAcceptance"/>): said, not counted in the image's colour nor in a refusal. Null otherwise.</param>
public sealed record ImageAdvisory(string Package, string Version, string Advisory, string Cve, string Severity, string FixedIn, string Url, string Ecosystem = "PyPI", string UbuntuProFixedIn = "", ImageAcceptance? Accepted = null);

/// <summary>
/// A known vulnerability a person accepted for an image: one nobody can mend
/// today (no version fixes it) in a part the image does not use, said with its
/// reason. While it has no fix it is shown, not raised: it gives the image no
/// colour and refuses nothing. The day a version fixes it, the acceptance stops
/// counting and it alarms again, for the image to be patched; any other
/// vulnerability alarms as ever. Kept by the agent in <c>image-acceptances.json</c>.
/// </summary>
/// <param name="Repository">The image without its tag, lower case (<c>ghcr.io/owner/app</c>): every version of it, the next one built too.</param>
/// <param name="Ecosystem">The package's kind, as <see cref="ImageAdvisory.Ecosystem"/>.</param>
/// <param name="Package">The package, as <see cref="ImageAdvisory.Package"/>.</param>
/// <param name="Advisory">The advisory, as <see cref="ImageAdvisory.Advisory"/> (<c>GHSA-f4j7-r4q5-qw2c</c>).</param>
/// <param name="Reason">Why it is accepted, as the person wrote it; never empty.</param>
/// <param name="By">Who accepted it: the user signed in, <c>local</c> at the agent's machine, or <c>token</c>.</param>
/// <param name="At">When.</param>
public sealed record ImageAcceptance(string Repository, string Ecosystem, string Package, string Advisory, string Reason, string By, DateTimeOffset At);

/// <summary>Answer of <c>GET /api/v1/image-security/acceptances</c>, and the agent's <c>image-acceptances.json</c>: every acceptance, of every image.</summary>
public sealed record ImageAcceptances(IReadOnlyList<ImageAcceptance>? Acceptances = null)
{
    /// <summary>The acceptances, none when the file has none; not written, being <see cref="Acceptances"/> again.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<ImageAcceptance> All => Acceptances ?? [];
}

/// <summary>Body of <c>POST /api/v1/image-security/acceptances</c>: accept one vulnerability of an image, with its reason.</summary>
/// <param name="Image">The image, as <see cref="ImageVulnerabilities.Image"/>; its tag does not matter, the acceptance is its repository's.</param>
/// <param name="Reason">Why; required.</param>
public sealed record AcceptImageVulnerability(string Image, string Ecosystem, string Package, string Advisory, string Reason);

/// <summary>
/// Answer of <c>GET /api/v1/image-security/vulnerabilities?image=</c> and of the
/// <c>image_vulnerabilities</c> tool: an image's packages, its Python libraries
/// and its distribution's, and every known vulnerability of them, the low ones
/// too, read as the image check reads them.
/// </summary>
/// <param name="Found">The image is on the machine; false, nothing else is said.</param>
/// <param name="Built">It was built on this machine (it has no digest of a registry's).</param>
/// <param name="HasPython">It has packages to read, Python libraries or a distribution's (the name is kept for older clients); false, nothing was checked, which is not a clean image.</param>
/// <param name="Checked">OSV.dev answered; false, <paramref name="Note"/> says why, and the libraries are listed alone.</param>
/// <param name="Refused">The agent makes no container from it: built here, a vulnerability moderate or worse, and Settings → Security set to Block.</param>
/// <param name="Remedies">A line per library with a vulnerability moderate or worse: how many, the worst, and the version that fixes them all, or that it has to be replaced.</param>
/// <param name="Patch">The image Zaindari's patch would build from this one (<c>POST /api/v1/image-security/patch</c>): <c>alpine:latest-patched</c>. Offered whenever the image was read, a fix published or not: the user's own commands (a library taken out) are a patch too.</param>
/// <param name="SystemUpgrade">The command the patch runs to upgrade the system packages (<c>apk upgrade --no-cache</c>, <c>apt-get update &amp;&amp; … apt-get upgrade -y …</c>); empty when no system package has a fix published, or the image has no package manager (distroless).</param>
/// <param name="PythonFixes">Each Python library with a fix published, at the version that fixes all of its own: <c>aiohttp&gt;=3.13.6</c>; for the patch window to offer.</param>
/// <param name="PythonInstall">The command that would raise them, by <paramref name="PythonInstaller"/>; a suggestion to read before it runs. Empty without <paramref name="PythonFixes"/>.</param>
/// <param name="Requirements">Every library line of the image's own <c>requirements*.txt</c> files, the vulnerable ones with the change Zaindari suggests: for the patch window to choose, line by line, what to raise, take to its latest or take out, and for the user's sources.</param>
/// <param name="PythonInstaller">The command the patch installs Python libraries with, always uv: <c>uv pip install --no-cache</c>, then the environment's own flags (<see cref="PythonEnvironment.Flags"/>), then the names; never <c>-U</c>, so what is installed and still meets a file stays as it is, and a library taken to its latest is named after <paramref name="PythonUpgradeOne"/>.</param>
/// <param name="PythonUninstaller">The command it takes them out with: <c>uv pip uninstall</c>, then the environment's flags, then the names.</param>
/// <param name="PythonSetup">The Dockerfile line that puts uv in an image that does not carry it, to go before the first <paramref name="PythonInstaller"/> or <paramref name="PythonUninstaller"/>: <c>COPY --from=ghcr.io/astral-sh/uv:… /uv /usr/local/bin/uv</c>; empty when the image has uv.</param>
/// <param name="PatchDockerfile">The Dockerfile the patch would build, as the patch window first shows it to be read and changed: <c>FROM</c> the image, <c>USER root</c>, its system upgrade when it has one, and its own user given back.</param>
/// <param name="RequirementFiles">The image's own <c>requirements*.txt</c> files, whole, comments and options kept: what the patch window rewrites by the choices made on <paramref name="Requirements"/>.</param>
/// <param name="PythonUpgradeOne">What takes one library to its latest and no other, its name to follow: <c>--upgrade-package</c>.</param>
/// <param name="PatchCheck">What Zaindari says of patching its Python libraries, before anything is chosen; null when it has none to patch.</param>
/// <param name="PythonEnvironments">The image's Python environments, each with the libraries installed in it: a library is installed, or taken out, in the one it is in, a requirements file in the one that holds most of its libraries (<see cref="PythonEnvironment.Of"/>).</param>
public sealed record ImageVulnerabilities(
    string Image,
    bool Found,
    bool Built,
    bool HasPython,
    bool Checked,
    bool Refused,
    IReadOnlyList<ImagePackage> Packages,
    IReadOnlyList<ImageAdvisory> Vulnerabilities,
    IReadOnlyList<string> Remedies,
    string Note = "",
    string Patch = "",
    string SystemUpgrade = "",
    IReadOnlyList<string>? PythonFixes = null,
    string PythonInstall = "",
    IReadOnlyList<ImageRequirement>? Requirements = null,
    string PythonInstaller = "",
    string PythonUninstaller = "",
    string PatchDockerfile = "",
    IReadOnlyList<ImageTextFile>? RequirementFiles = null,
    string PythonSetup = "",
    IReadOnlyList<PythonEnvironment>? PythonEnvironments = null,
    string PythonUpgradeOne = "",
    PatchCheck? PatchCheck = null);

/// <summary>
/// What Zaindari says of patching an image's Python libraries, read from the
/// image before anything is chosen: <see cref="Clear"/>, nothing to be careful
/// with; <see cref="Care"/>, it can be patched knowing what <see cref="Notes"/>
/// says; <see cref="Refused"/>, it cannot be, and why.
/// </summary>
/// <param name="Notes">What to be careful with, or why it cannot be patched, a line each.</param>
public sealed record PatchCheck(string Verdict, IReadOnlyList<string> Notes)
{
    public const string Clear = "clear";
    public const string Care = "care";
    public const string Refused = "refused";

    /// <summary>The verdict as a sentence.</summary>
    public string Said => Verdict switch
    {
        Clear => "Its Python libraries can be patched: nothing in the image calls for care.",
        Care => "Its Python libraries can be patched with care: read what follows, and try the patch before it is built.",
        _ => "Its Python libraries cannot be patched by Zaindari:",
    };
}

/// <summary>
/// A Python environment of an image: a virtual environment (<c>/opt/venv-a0</c>),
/// or a Python of the system's, which a distribution may mark as its own
/// (externally managed) and refuse to install into without being told.
/// </summary>
/// <param name="Python">Its interpreter: <c>/opt/venv-a0/bin/python3.12</c>.</param>
/// <param name="Venv">A virtual environment; false for a Python of the system's.</param>
/// <param name="Libraries">The libraries installed in it, by their names as PyPI compares them (<see cref="Normalized"/>).</param>
/// <param name="Builds">Those installed as a build for one variant (<c>torch 2.4.0+cpu</c>), which PyPI does not have.</param>
public sealed record PythonEnvironment(string Python, bool Venv, IReadOnlyList<string> Libraries, IReadOnlyList<PythonBuild>? Builds = null)
{
    /// <summary>What tells uv the environment to work in: its interpreter, and, for a Python of the system's, that the distribution's mark is to be passed over.</summary>
    public string Flags => $"--python {Python}{(Venv ? "" : " --break-system-packages")}";

    /// <summary>
    /// What an install in it adds after <see cref="Flags"/>: the index of each
    /// of its variant builds, for uv to take the best version of every library
    /// from PyPI and those, so a build for one variant that has to change
    /// stays one (a <c>+cpu</c> build is newer than PyPI's of the same version,
    /// the one for CUDA); empty when it has none whose index is known.
    /// </summary>
    public string Sources =>
        (Builds ?? []).Select(build => build.Index).Where(index => index.Length > 0).Distinct(StringComparer.Ordinal).ToList() is { Count: > 0 } indexes
            ? string.Concat(indexes.Select(index => $" --extra-index-url {index}")) + " --index-strategy unsafe-best-match"
            : "";

    /// <summary><paramref name="library"/> is installed in it.</summary>
    public bool Holds(string library) => Libraries.Contains(Normalized(library));

    /// <summary><paramref name="library"/>'s build for one variant, when it is one.</summary>
    public PythonBuild? BuildOf(string library) => (Builds ?? []).FirstOrDefault(build => build.Library == Normalized(library));

    /// <summary>
    /// The environment that holds most of <paramref name="libraries"/>: the one
    /// a requirements file is of; null when none holds any of them (a file of
    /// a tool's, not the application's).
    /// </summary>
    public static PythonEnvironment? Of(IEnumerable<PythonEnvironment> environments, IEnumerable<string> libraries)
    {
        var wanted = libraries.Select(Normalized).ToHashSet(StringComparer.Ordinal);
        return environments
            .Select(environment => (Environment: environment, Held: environment.Libraries.Count(wanted.Contains)))
            .Where(candidate => candidate.Held > 0)
            .OrderByDescending(candidate => candidate.Held)
            .Select(candidate => candidate.Environment)
            .FirstOrDefault();
    }

    /// <summary>PyPI's names compare as PEP 503 says: case and <c>_</c>, <c>-</c>, <c>.</c> alike; extras left out.</summary>
    public static string Normalized(string name) =>
        name.Split('[')[0].Trim().ToLowerInvariant().Replace('_', '-').Replace('.', '-');
}

/// <summary>Body of <c>POST /api/v1/image-security/patch/dry-run</c>: the patch's Python lines, tried in a container of the image before it is built.</summary>
/// <param name="Commands">Its <c>uv pip install</c> and <c>uv pip uninstall</c> lines, as the Dockerfile runs them, without <c>RUN</c>.</param>
public sealed record PatchDryRunRequest(string Image, IReadOnlyList<string> Commands);

/// <summary>What the patch's Python lines would do, each tried: nothing is written.</summary>
public sealed record PatchDryRun(IReadOnlyList<PatchDryRunStep> Steps)
{
    /// <summary>None would fail.</summary>
    public bool Ok => Steps.All(step => step.Error.Length == 0);
}

/// <summary>One line of the patch, tried.</summary>
/// <param name="Command">The line, as the patch runs it.</param>
/// <param name="Changes">What it would change, a library a line: <c>torch 2.4.0+cpu → 2.14.1+cpu</c>, <c>new: httpx2 2.16.0</c>, <c>out: python-jose</c>.</param>
/// <param name="Warnings">What calls for care: a build for one variant that would become another, NVIDIA's libraries it would bring, libraries the image does not have.</param>
/// <param name="Error">Why it would fail, as uv says it; empty when it would not.</param>
/// <param name="Output">What uv said, whole.</param>
public sealed record PatchDryRunStep(string Command, IReadOnlyList<string> Changes, IReadOnlyList<string> Warnings, string Error, IReadOnlyList<string> Output);

/// <summary>A library installed as a build for one variant: <c>torch</c> <c>2.4.0+cpu</c>.</summary>
/// <param name="Library">Its name, as PyPI compares them.</param>
/// <param name="Version">Its version, the variant after the <c>+</c>.</param>
/// <param name="Index">Where builds for that variant come from (<c>https://download.pytorch.org/whl/cpu</c>); empty when it is not known, and it is not to be changed.</param>
public sealed record PythonBuild(string Library, string Version, string Index);

/// <summary>A text file of an image, as a path in it and its text: a <c>requirements.txt</c>.</summary>
public sealed record ImageTextFile(string Path, string Text);

/// <summary>A file the patch is built with, beside its Dockerfile, which <c>COPY</c>s it in by <paramref name="Name"/>: a <c>requirements.txt</c> rewritten.</summary>
/// <param name="Name">A plain file name, no folder: <c>requirements.txt</c>.</param>
public sealed record PatchFile(string Name, string Text);

/// <summary>A library line of an image's <c>requirements*.txt</c>, and, when the library is vulnerable, what Zaindari suggests it say.</summary>
/// <param name="File">The file, as a path in the image: <c>/backend/requirements.txt</c>.</param>
/// <param name="Line">The line as it is: <c>pillow==12.2.0</c>.</param>
/// <param name="Suggested">The line it would be, the library at the version that fixes all of its own: <c>pillow&gt;=13.0.0</c>; empty when it is not vulnerable, or no version fixes it.</param>
/// <param name="Note">Why: what the version fixes, or that nothing does yet and it is to be replaced or taken out; empty when it is not vulnerable.</param>
/// <param name="Library">The library the line names, with its extras: <c>uvicorn[standard]</c>.</param>
/// <param name="Vulnerable">Zaindari found a known vulnerability in it.</param>
public sealed record ImageRequirement(string File, string Line, string Suggested, string Note, string Library = "", bool Vulnerable = false);

/// <summary>
/// Body of <c>POST /api/v1/image-security/patch</c>: build the image's patch, an
/// image of its own with its system packages upgraded, as a Windows update
/// patches Windows, then move the containers named to it.
/// </summary>
/// <param name="Image">The image to patch, as <see cref="ImageVulnerabilities.Image"/>.</param>
/// <param name="Containers">The containers, by id or name, recreated on the patch once it is built; none, the patch is only built.</param>
/// <param name="System">Upgrade the system packages (<see cref="ImageVulnerabilities.SystemUpgrade"/>).</param>
/// <param name="Commands">The user's own, one per line, each run as root after the system's (<c>RUN</c> added when the line does not start with it): a Python library raised, anything the image needs.</param>
/// <param name="Dockerfile">The patch's whole Dockerfile, as the patch window shows it and the user left it: built as it is, <paramref name="System"/> and <paramref name="Commands"/> unread.</param>
/// <param name="Files">Files written beside the Dockerfile, for it to <c>COPY</c> in: the requirements files the patch window rewrote.</param>
public sealed record PatchImageRequest(string Image, IReadOnlyList<string> Containers, bool System = true, string Commands = "", string Dockerfile = "", IReadOnlyList<PatchFile>? Files = null);

/// <summary>
/// How Zaindari, the image check, says how bad a library is: in the line it
/// writes per library (<c>, the worst high;</c>), and in the level that line
/// and each vulnerability are shown at, high and critical as errors (red),
/// moderate as a warning (yellow), the rest as information. Written by the
/// agent and read by every client, so the two never part.
/// </summary>
public static class ImageRemedies
{
    /// <summary>
    /// Whether a vulnerability can be accepted: only while no version fixes it,
    /// in the free archive or in Ubuntu Pro. One with a fix is mended, by a
    /// patch, never accepted; an acceptance whose vulnerability gets a fix stops
    /// counting by the same rule.
    /// </summary>
    public static bool Acceptable(string fixedIn, string ubuntuProFixedIn) => fixedIn.Length == 0 && ubuntuProFixedIn.Length == 0;

    /// <summary>How a library's line says its worst severity, before the severity itself.</summary>
    public const string WorstSaid = ", the worst ";

    /// <summary>The level a vulnerability of this severity is shown at.</summary>
    public static string LevelOf(string severity) => severity switch
    {
        "critical" or "high" => "ERROR",
        "moderate" => "WARNING",
        _ => "INFO",
    };

    /// <summary>
    /// What is left in an image, in one line, for the patch's log and Zaindari's
    /// window alike: how many of its packages still have a fix a patch can
    /// install; or, when none has, that nothing is left to patch, the system's
    /// left to its distribution and a Python library with no fix to be
    /// replaced or taken out, by name. Empty when nothing is known against it.
    /// </summary>
    public static string WhatIsLeft(IReadOnlyCollection<ImageAdvisory> advisories)
    {
        if (advisories.Count == 0)
        {
            return "";
        }

        static int Packages(IEnumerable<ImageAdvisory> some) => some.Select(a => a.Package).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        static string Counted(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";
        var fixable = advisories.Where(a => a.FixedIn.Length > 0).ToList();
        if (fixable.Count > 0)
        {
            return $"Still with a fix published: {Counted(Packages(fixable.Where(a => a.Ecosystem != "PyPI")), "system package", "system packages")} and "
                + $"{Counted(Packages(fixable.Where(a => a.Ecosystem == "PyPI")), "Python library", "Python libraries")}: a patch can mend them.";
        }

        var system = advisories.Where(a => a.Ecosystem != "PyPI").ToList();
        var python = advisories.Where(a => a.Ecosystem == "PyPI").Select(a => a.Package).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var distribution = system.Select(a => a.Ecosystem.Split(':')[0]).FirstOrDefault() ?? "";
        var paid = system.Where(a => a.UbuntuProFixedIn.Length > 0).ToList();
        return "Nothing left has a fix published, so no patch can mend more today."
            + (system.Count > 0 ? $" {Counted(system.Count, "vulnerability", "vulnerabilities")} in system packages, which {distribution} has not fixed yet." : "")
            + (paid.Count > 0 ? $" {paid.Count} of them, in {Counted(Packages(paid), "package", "packages")}, {(paid.Count == 1 ? "is" : "are")} fixed only in {UbuntuPro}, Canonical's subscription." : "")
            + (python.Count > 0 ? $" Python, with no version that fixes it: {string.Join(", ", python)}; replace it, or take it out." : " No Python library is left to mend.");
    }

    /// <summary>The subscription that fixes some of Ubuntu's packages the free archive does not.</summary>
    public const string UbuntuPro = "Ubuntu Pro";

    /// <summary>What fixes one vulnerability, as each of its lines says it: the version, or the one only Ubuntu Pro has, or that none does yet.</summary>
    public static string FixSaid(string fixedIn, string ubuntuProFixedIn) =>
        fixedIn.Length > 0 ? $"fixed in {fixedIn}"
        : ubuntuProFixedIn.Length > 0 ? $"fixed only in {UbuntuPro} ({ubuntuProFixedIn}), not in the free archive"
        : "no version fixes it yet";

    /// <summary>The level a library's line is shown at: its worst severity's, a warning at least.</summary>
    public static string LevelOfLine(string line)
    {
        var at = line.IndexOf(WorstSaid, StringComparison.Ordinal);
        return at >= 0 && LevelOf(line[(at + WorstSaid.Length)..].Split(';', 2)[0]) == "ERROR" ? "ERROR" : "WARNING";
    }
}
