using WslcAgent.Mcp;
using WslcAgent.Server.Sessions;

namespace WslcAgent.Server.Containers;

/// <summary>
/// Once, shortly after the agent starts:
/// <see cref="RestartPolicyReconciler"/> starts every enrolled container the
/// policy says should run. The other time the policy is applied is when a
/// session is started from the app, which <c>SessionService</c> does.
/// <para>
/// Starting a container starts its session, since <c>wslc</c> starts the
/// session a command is sent to. With Settings → Security → Session at agent
/// start switched off, a session found stopped is left as it is: nothing is
/// sent to it.
/// </para>
/// </summary>
public sealed class RestartReconciler(
    RestartPolicyReconciler reconciler,
    AgentStartSettingsStore settings,
    ISessionService sessions,
    ILogger<RestartReconciler> logger) : BackgroundService
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Delay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!settings.Get().StartStoppedSession && !await SessionRunningAsync(stoppingToken))
        {
            logger.LogInformation("the session is stopped and Settings says not to start it when the agent starts: the restart policies wait for it to be started");
            return;
        }

        await reconciler.ReconcileAsync(cancellationToken: stoppingToken);
    }

    /// <summary>Whether the session the agent targets runs now; a look that fails says it does not, so nothing is started on a guess.</summary>
    private async Task<bool> SessionRunningAsync(CancellationToken cancellationToken)
    {
        try
        {
            var list = await sessions.ListAsync(cancellationToken);
            return list.Sessions.Any(session => session.Active && session.Name == list.Selected);
        }
        catch (Exception failed) when (failed is not OperationCanceledException)
        {
            logger.LogWarning("could not tell whether the session is running: {Message}", failed.Message);
            return false;
        }
    }
}