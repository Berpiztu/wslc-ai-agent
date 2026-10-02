using WslcAgent.Server.Notifications;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// Pushes the agent owns (<see cref="ImageJobs"/>), so a publish can close its
/// dialog: the push console shows the upload, stops it, and its end is notified.
/// </summary>
public sealed class ImagePushes(IWslcRunner wslc, ICliActivity activity, Notifier notifier, ILogger<ImagePushes> logger)
    : ImageJobs(wslc, activity, notifier, logger)
{
    protected override string Verb => "Push";

    protected override string DoneStatus => "Pushed";

    protected override IReadOnlyList<string> Command { get; } = ["image", "push"];

    protected override (int Pct, string Status) Read(string output) => PushProgress.Read(output);
}
