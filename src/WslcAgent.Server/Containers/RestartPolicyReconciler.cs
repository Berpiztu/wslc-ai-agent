using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Notifications;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// One pass of the restart policy: <c>container start</c> for every enrolled
/// container the policy says should run. "Already running" answers are not
/// failures, and one container that will not start does not stop the rest —
/// but it is notified, since nobody is watching when the machine starts.
/// </summary>
public sealed class RestartPolicyReconciler(RestartPolicyStore policies, IWslcRunner wslc, Notifier notifier, ILogger<RestartPolicyReconciler> logger)
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
        foreach (var key in policies.KeysToStart())
        {
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

        return started;
    }
}
