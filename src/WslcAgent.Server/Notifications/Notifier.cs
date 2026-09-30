using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Notifications;

/// <summary>
/// Where every notification is raised: what
/// Settings › Notifications switched off goes no further; the rest is kept
/// (<see cref="NotificationHistory"/>), written to the agent's log,
/// announced on the events stream, so whoever shows notifications — the tray
/// icon — reads it at once, and pushed to the registered phones
/// (<see cref="FirebasePush"/>).
/// </summary>
public sealed class Notifier(NotificationSettingsStore settings, NotificationHistory history, WslcEvents events, FirebasePush push, ILogger<Notifier> logger) : INotificationService
{
    public NotificationSettings Settings() => settings.Get();

    public NotificationSettings Save(NotificationSettings saved) => settings.Set(saved);

    public NotificationList After(long after) => history.After(after);

    /// <summary>What a notification's line in the agent's log begins with, its area in the brackets after it.</summary>
    public const string LogPrefix = "notification [";

    /// <summary>Raises a notification, unless its kind is switched off.</summary>
    /// <param name="link">The page it opens, relative to the agent; empty for none.</param>
    /// <param name="area">Where its line goes on the Logs page; null for its kind's own.</param>
    /// <param name="action">Its button, one of <see cref="NotificationAction"/>; empty for none.</param>
    public void Raise(string kind, string severity, string title, string text, string link = "", string? area = null, string action = "")
    {
        if (!settings.Get().Allows(kind))
        {
            return;
        }

        var notification = history.Add(kind, severity, title, text, link, action);
        logger.Log(
            severity == NotificationSeverity.Info ? LogLevel.Information : LogLevel.Warning,
            LogPrefix + "{Area}] {Id}: {Title} — {Text}", area ?? AreaOf(kind), notification.Id, title, text);
        events.Publish(new ChangeNotice([ChangeNotice.Notification]));
        push.Send(notification);
    }

    /// <summary>
    /// A notification's line on the Logs page goes with what raised it: a
    /// container's under Containers, an image's
    /// under Images, the host's under General, which is the system's too.
    /// </summary>
    private static string AreaOf(string kind) => kind switch
    {
        NotificationKind.ContainerStopped or NotificationKind.ContainerNotRestarted
            or NotificationKind.ContainerCpu or NotificationKind.ContainerMemory => CliTraceDescription.Containers,
        // With the update's own lines, under File transfers (AgentUpdater.LogPrefix).
        NotificationKind.UpdateFailed or NotificationKind.UpdateInstalled
            or NotificationKind.UpdateAnnounced or NotificationKind.UpdateCancelled => CliTraceDescription.Transfers,
        _ => CliTraceDescription.General,
    };

    /// <summary>The area a notification's line names, or null when the line is not one.</summary>
    public static string? AreaIn(string message)
    {
        if (!message.StartsWith(LogPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var end = message.IndexOf(']', LogPrefix.Length);
        return end > LogPrefix.Length ? message[LogPrefix.Length..end] : null;
    }

    /// <summary>
    /// One of the agent's jobs ended, failed or finished, however short: a
    /// minimum of a minute left two small files uploaded without a word with
    /// "A job finished" ticked.
    /// </summary>
    /// <param name="job">What it was: Transfer, Pull, Build, Backup.</param>
    /// <param name="subject">What it was of: the file and its container, the image.</param>
    /// <param name="error">Why it failed; null when it finished.</param>
    /// <param name="took">How long it ran.</param>
    /// <param name="link">The page that shows it.</param>
    /// <param name="area">Where its line goes on the Logs page: a transfer's under File transfers, a pull's under Images.</param>
    public void JobEnded(string job, string subject, string? error, TimeSpan took, string link, string area)
    {
        if (error is not null)
        {
            Raise(NotificationKind.JobFailed, NotificationSeverity.Error, $"{job} failed", $"{subject}: {error}", link, area);
        }
        else
        {
            Raise(NotificationKind.JobFinished, NotificationSeverity.Info, $"{job} finished", $"{subject}, in {Spent(took)}", link, area);
        }
    }

    private static string Spent(TimeSpan took) =>
        took.TotalHours >= 1 ? $"{(int)took.TotalHours} h {took.Minutes} min"
        : took.TotalMinutes >= 1 ? $"{(int)took.TotalMinutes} min {took.Seconds} s"
        : $"{Math.Max(1, (int)took.TotalSeconds)} s";
}
