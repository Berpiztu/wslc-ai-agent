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
/// <param name="Patch">The image Zaindari's patch would build from this one (<c>POST /api/v1/image-security/patch</c>), its system packages upgraded: <c>alpine:latest-patched</c>. Empty when there is nothing to patch: no system package with a fix published, or no package manager in the image to install it with.</param>
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
    string Patch = "");

/// <summary>
/// Body of <c>POST /api/v1/image-security/patch</c>: build the image's patch, an
/// image of its own with its system packages upgraded, as a Windows update
/// patches Windows, then move the containers named to it.
/// </summary>
/// <param name="Image">The image to patch, as <see cref="ImageVulnerabilities.Image"/>.</param>
/// <param name="Containers">The containers, by id or name, recreated on the patch once it is built; none, the patch is only built.</param>
public sealed record PatchImageRequest(string Image, IReadOnlyList<string> Containers);

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

    /// <summary>The level a library's line is shown at: its worst severity's, a warning at least.</summary>
    public static string LevelOfLine(string line)
    {
        var at = line.IndexOf(WorstSaid, StringComparison.Ordinal);
        return at >= 0 && LevelOf(line[(at + WorstSaid.Length)..].Split(';', 2)[0]) == "ERROR" ? "ERROR" : "WARNING";
    }
}
