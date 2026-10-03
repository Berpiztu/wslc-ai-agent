using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// One <c>wslc container restart</c> (WSL 2.9.12). An older CLI has no such
/// verb and answers <c>Unrecognized command</c>; the stop + start pair it
/// replaced is the fallback, so the verb works on every WSL the agent
/// supports. Either way the container ends running.
/// </summary>
public sealed class ContainerRestarter(IWslcRunner wslc, RestartPolicyStore policies, ILogger<ContainerRestarter> logger)
{
    public async Task RestartAsync(string container, CancellationToken cancellationToken = default)
    {
        var id = WslcArgs.Require(container, "container");
        try
        {
            await wslc.RunAsync(["container", "restart", id], TimeSpan.FromSeconds(120), cancellationToken);
        }
        catch (WslcException ex) when (ex.IsUnrecognizedCommand)
        {
            try
            {
                await wslc.RunAsync(["container", "stop", id], TimeSpan.FromSeconds(90), cancellationToken);
            }
            catch (WslcException stop)
            {
                // Already stopped (or never started): start is still the goal.
                logger.LogInformation("restart: stop of {Container} skipped: {Message}", id, stop.Message);
            }

            await wslc.RunAsync(["container", "start", id], cancellationToken: cancellationToken);
        }

        policies.SetDesired(id, RestartPolicyInfo.Running);
    }
}
