using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Notifications;
using WslcAgent.Server.Projects;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// One pass of the restart policy: <c>container start</c> for every enrolled
/// container the policy says should run. "Already running" answers are not
/// failures, and one container that will not start does not stop the rest —
/// but it is notified, since nobody is watching when the machine starts.
/// A project's container is started only once the services its own depends
/// on run, as every start through the agent is (<see cref="ProjectDependencies"/>):
/// one whose dependency is missing from its project, however another
/// container is named, is not started, so it does not send its work to what
/// is not its own. The pass goes round while it starts something, so one
/// listed before what it depends on waits for it instead of being refused.
/// </summary>
public sealed class RestartPolicyReconciler(
    RestartPolicyStore policies,
    IWslcRunner wslc,
    ProjectDependencies dependencies,
    StartFailures failures,
    Notifier notifier,
    ILogger<RestartPolicyReconciler> logger)
{
    /// <summary>
    /// Runs the pass and returns the containers it started (the ones that were
    /// already up, or failed, are not in it).
    /// </summary>
    /// <param name="session">
    /// The session to start them in: null for the one the agent targets, a name
    /// to override it, an empty string for the CLI default — as
    /// <see cref="IWslcRunner.RunAsync"/> reads it.
    /// </param>
    public async Task<IReadOnlyList<string>> ReconcileAsync(string? session = null, CancellationToken cancellationToken = default)
    {
        var started = new List<string>();
        var waiting = policies.KeysToStart().ToList();
        var refused = new Dictionary<string, string>(StringComparer.Ordinal);
        bool moved;
        do
        {
            moved = false;
            foreach (var key in waiting.ToList())
            {
                try
                {
                    await dependencies.RequireRunningAsync(key, cancellationToken);
                }
                catch (InvalidOperationException ex)
                {
                    // Its dependency may be further down the list: asked again on the next round.
                    refused[key] = ex.Message;
                    continue;
                }

                waiting.Remove(key);
                refused.Remove(key);
                moved = true;
                try
                {
                    await wslc.RunAsync(["container", "start", key], cancellationToken: cancellationToken, session: session);
                    logger.LogInformation("restart policy started {Container}", key);
                    started.Add(key);
                }
                catch (WslcException ex) when (ex.Message.Contains("running", StringComparison.OrdinalIgnoreCase))
                {
                    // Already up: nothing to do.
                }
                catch (WslcException ex)
                {
                    logger.LogWarning("restart policy could not start {Container}: {Message}", key, ex.Message);
                    notifier.Raise(NotificationKind.ContainerNotRestarted, NotificationSeverity.Error, $"{key} not started",
                        $"Its restart policy could not start it: {ex.Message}", NotificationLink.Containers);
                }
            }
        }
        while (moved && waiting.Count > 0);

        foreach (var (key, why) in refused)
        {
            // Kept as a refused start is, so the container says it in red (StartFailures).
            failures.Failed(key, why);
            logger.LogWarning("restart policy did not start {Container}: {Message}", key, why);
            notifier.Raise(NotificationKind.ContainerNotRestarted, NotificationSeverity.Error, $"{key} not started",
                $"Its restart policy did not start it: {why}", NotificationLink.Containers);
        }

        return started;
    }
}
