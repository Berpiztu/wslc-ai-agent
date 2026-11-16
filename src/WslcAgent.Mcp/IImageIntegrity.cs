using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>Whether the images on the selected session are whole. Implemented by the server.</summary>
public interface IImageIntegrity
{
    /// <summary>
    /// Checks <paramref name="image"/>, or every image when none is named,
    /// against the session's record of its layers. Reads only.
    /// <see cref="KeyNotFoundException"/> for an image that is not there;
    /// <see cref="InvalidOperationException"/> when the session's store cannot
    /// be read the way the agent knows.
    /// </summary>
    Task<ImageChecks> CheckAsync(string? image = null, CancellationToken cancellationToken = default);
}