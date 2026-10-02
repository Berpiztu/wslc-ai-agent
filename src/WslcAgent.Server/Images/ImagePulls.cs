using WslcAgent.Server.Notifications;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// Pulls the agent owns (<see cref="ImageJobs"/>): the Images table shows their
/// progress, stops one, and opens its live output.
/// </summary>
public sealed class ImagePulls(IWslcRunner wslc, ICliActivity activity, Notifier notifier, ILogger<ImagePulls> logger)
    : ImageJobs(wslc, activity, notifier, logger)
{
    protected override string Verb => "Pull";

    protected override string DoneStatus => "Downloaded";

    protected override IReadOnlyList<string> Command { get; } = ["image", "pull"];

    protected override (int Pct, string Status) Read(string output) => PullProgress.Read(output);
}
