namespace WslcAgent.Server.Containers;

/// <summary>
/// Once, shortly after the agent starts:
/// <see cref="RestartPolicyReconciler"/> starts every enrolled container the
/// policy says should run. The other time the policy is applied is when a
/// session is started from the app, which <c>SessionService</c> does.
/// </summary>
public sealed class RestartReconciler(RestartPolicyReconciler reconciler) : BackgroundService
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

        await reconciler.ReconcileAsync(cancellationToken: stoppingToken);
    }
}
