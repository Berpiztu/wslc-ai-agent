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

/// <summary>A Python library an image carries, as <c>pip list</c> reads it in the image itself.</summary>
public sealed record ImagePackage(string Name, string Version);

/// <summary>A known vulnerability of one library of an image, as OSV.dev gives it.</summary>
/// <param name="Severity"><c>low</c>, <c>moderate</c>, <c>high</c>, <c>critical</c>, or <c>unknown</c> when neither the advisory, its GHSA alias nor its CVSS vector gives one.</param>
/// <param name="FixedIn">The first version that fixes it; empty when none does yet.</param>
/// <param name="Url">The advisory on osv.dev.</param>
public sealed record ImageAdvisory(string Package, string Version, string Advisory, string Cve, string Severity, string FixedIn, string Url);

/// <summary>
/// Answer of <c>GET /api/v1/image-security/vulnerabilities?image=</c> and of the
/// <c>image_vulnerabilities</c> tool: an image's Python libraries and every
/// known vulnerability of them, the low ones too, read as the image check
/// reads them.
/// </summary>
/// <param name="Found">The image is on the machine; false, nothing else is said.</param>
/// <param name="Built">It was built on this machine (it has no digest of a registry's).</param>
/// <param name="HasPython">It has a Python with pip to read; false, it has nothing to check.</param>
/// <param name="Checked">OSV.dev answered; false, <paramref name="Note"/> says why, and the libraries are listed alone.</param>
/// <param name="Refused">The agent makes no container from it: built here, a vulnerability moderate or worse, and Settings → Security set to Block.</param>
/// <param name="Remedies">A line per library with a vulnerability moderate or worse: how many, the worst, and the version that fixes them all, or that it has to be replaced.</param>
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
    string Note = "");
