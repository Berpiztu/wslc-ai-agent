using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>An image's Python libraries and their known vulnerabilities, as the image check reads them. Implemented by the server.</summary>
public interface IImageSecurity
{
    /// <summary>Every library and every known vulnerability of them, the low ones too; nothing is refused or marked by asking.</summary>
    Task<ImageVulnerabilities> ReportAsync(string image, CancellationToken cancellationToken = default);

    /// <summary>What an image's patch changed, from the image to the patch, package by package, with the vulnerabilities each change fixed; <paramref name="image"/> either of them.</summary>
    Task<PatchChanges> ChangesAsync(string image, CancellationToken cancellationToken = default);
}
