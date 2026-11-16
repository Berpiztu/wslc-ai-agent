using WslcAgent.Server.Notifications;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// Pulls the agent owns (<see cref="ImageJobs"/>): the Images table shows their
/// progress, stops one, and opens its live output.
/// </summary>
public sealed class ImagePulls(IWslcRunner wslc, ICliActivity activity, Notifier notifier, ILogger<ImagePulls> logger, ImageIntegrity? integrity = null)
    : ImageJobs(wslc, activity, notifier, logger)
{
    /// <summary>The image a pull left is checked before the pull is called done.</summary>
    protected override Task<string?> DamagedAsync(string image) =>
        integrity is null ? Task.FromResult<string?>(null) : integrity.WhyDamagedAsync(image);

    protected override string Verb => "Pull";

    protected override string DoneStatus => "Downloaded";

    protected override IReadOnlyList<string> Command { get; } = ["image", "pull"];

    protected override (int Pct, string Status) Read(string output) => PullProgress.Read(output);
}
