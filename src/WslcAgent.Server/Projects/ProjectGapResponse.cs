using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Notifications;

namespace WslcAgent.Server.Projects;

/// <summary>
/// A project changed around the agent (<see cref="ProjectGaps.Opened"/>: one
/// of its services lost its container, a script having removed it to run
/// one of its own under the name) is answered as an update of the project
/// would be: what ran is started again (<see cref="ProjectLifecycle.RenewAsync"/>).
/// What depends on the missing service then does not start, so it sends its
/// work to nothing that is not its own, a container that took the name
/// included. Said in the log and as a notification, since nobody may be
/// watching.
/// </summary>
public sealed class ProjectGapResponse(ProjectGaps gaps, ProjectLifecycle lifecycle, Notifier notifier, ILogger<ProjectGapResponse> logger) : IHostedService
{
    /// <summary>The projects being answered now: a change seen again meanwhile waits for this answer, not a second one.</summary>
    private readonly HashSet<string> _answering = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        gaps.Opened += OnOpened;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        gaps.Opened -= OnOpened;
        return Task.CompletedTask;
    }

    private void OnOpened(ProjectGap gap)
    {
        lock (_gate)
        {
            if (!_answering.Add(gap.Project))
            {
                return;
            }
        }

        _ = Task.Run(() => AnswerAsync(gap));
    }

    private async Task AnswerAsync(ProjectGap gap)
    {
        try
        {
            logger.LogWarning("project {Project} changed around the agent: {Gap} What runs of it is started again, as an update would.", gap.Project, gap.Text);
            var notStarted = await lifecycle.RenewAsync(gap.Project);
            if (notStarted.Count > 0)
            {
                var text = $"{gap.Text} Started again as an update would be, and not started: {string.Join("; ", notStarted)}";
                logger.LogWarning("project {Project}: {Text}", gap.Project, text);
                notifier.Raise(NotificationKind.ContainerNotRestarted, NotificationSeverity.Error, $"{gap.Project} changed around the agent", text, NotificationLink.Containers);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogError("project {Project} changed around the agent, and could not be started again: {Message}", gap.Project, ex.Message);
        }
        finally
        {
            lock (_gate)
            {
                _answering.Remove(gap.Project);
            }
        }
    }
}
