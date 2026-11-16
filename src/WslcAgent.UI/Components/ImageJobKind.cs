using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>The jobs on an image the agent owns and a page follows: one console, one cancel, one cross for the three.</summary>
public enum ImageJobKind
{
    Pull,
    Push,
    Check,
}

/// <summary>What differs between those jobs: the word for each, and the agent's calls.</summary>
public static class ImageJobKinds
{
    /// <summary>The job's name, as its window's title and its messages say it.</summary>
    public static string Verb(this ImageJobKind kind) => kind switch
    {
        ImageJobKind.Push => "Push",
        ImageJobKind.Check => "Check",
        _ => "Pull",
    };

    public static Task<ImagePullLog> LogAsync(this WslcAgentApi api, ImageJobKind kind, string image, CancellationToken cancellationToken = default) => kind switch
    {
        ImageJobKind.Push => api.GetImagePushLogAsync(image, cancellationToken),
        ImageJobKind.Check => api.GetImageCheckLogAsync(image, cancellationToken),
        _ => api.GetImagePullLogAsync(image, cancellationToken),
    };

    public static Task<CancelImagePullResult> CancelAsync(this WslcAgentApi api, ImageJobKind kind, string image, CancellationToken cancellationToken = default) => kind switch
    {
        ImageJobKind.Push => api.CancelImagePushAsync(image, cancellationToken),
        ImageJobKind.Check => api.CancelImageCheckAsync(image, cancellationToken),
        _ => api.CancelImagePullAsync(image, cancellationToken),
    };
}