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
public sealed record ImageAdvisory(string Package, string Version, string Advisory, string Cve, string Severity, string FixedIn, string Url, string Ecosystem = "PyPI");

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
/// <param name="PythonInstall">The command that would raise them, with the installer the image carries (<c>python3 -m pip install …</c> or <c>uv pip install --system …</c>); a suggestion to read before it runs. Empty without <paramref name="PythonFixes"/>.</param>
/// <param name="Requirements">Every library line of the image's own <c>requirements*.txt</c> files, the vulnerable ones with the change Zaindari suggests: for the patch window to choose, line by line, what to raise, take to its latest or take out, and for the user's sources.</param>
/// <param name="PythonInstaller">The command the image installs Python libraries with, the names to follow: <c>python3 -m pip install --no-cache-dir -U</c>, or <c>uv pip install --system --no-cache -U</c> whenever it has uv.</param>
/// <param name="PythonUninstaller">The command it takes them out with, the names to follow: <c>python3 -m pip uninstall -y</c>, or <c>uv pip uninstall --system</c>.</param>
/// <param name="PatchDockerfile">The Dockerfile the patch would build, as the patch window first shows it to be read and changed: <c>FROM</c> the image, <c>USER root</c>, its system upgrade when it has one, and its own user given back.</param>
/// <param name="RequirementFiles">The image's own <c>requirements*.txt</c> files, whole, comments and options kept: what the patch window rewrites by the choices made on <paramref name="Requirements"/>.</param>
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
    IReadOnlyList<ImageTextFile>? RequirementFiles = null);

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
        return "Nothing left has a fix published, so no patch can mend more today."
            + (system.Count > 0 ? $" {Counted(system.Count, "vulnerability", "vulnerabilities")} in system packages, which {distribution} has not fixed yet." : "")
            + (python.Count > 0 ? $" Python, with no version that fixes it: {string.Join(", ", python)}; replace it, or take it out." : " No Python library is left to mend.");
    }

    /// <summary>The level a library's line is shown at: its worst severity's, a warning at least.</summary>
    public static string LevelOfLine(string line)
    {
        var at = line.IndexOf(WorstSaid, StringComparison.Ordinal);
        return at >= 0 && LevelOf(line[(at + WorstSaid.Length)..].Split(';', 2)[0]) == "ERROR" ? "ERROR" : "WARNING";
    }
}
