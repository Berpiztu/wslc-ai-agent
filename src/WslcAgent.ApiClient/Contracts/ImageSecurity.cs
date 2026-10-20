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
