using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>Running a command inside a container: one shot here, interactively in <see cref="ExecTerminals"/>.</summary>
public sealed partial class ContainerService
{
    private static readonly TimeSpan ExecTimeout = TimeSpan.FromMinutes(5);

    public async Task<ContainerExecResult> ExecAsync(string container, string command, CancellationToken cancellationToken = default)
    {
        var argv = ShellWords.Split(command);
        if (argv.Count == 0)
        {
            throw new ArgumentException("A command is required.", nameof(command));
        }

        List<string> args = ["exec", WslcArgs.Require(container, "container"), .. argv];
        try
        {
            var result = await wslc.RunAsync(args, ExecTimeout, cancellationToken);
            return new ContainerExecResult(result.Stdout, result.Stderr, result.ExitCode);
        }
        catch (WslcException ex)
        {
            // The command failed inside the container: that is its answer, not an agent error.
            return new ContainerExecResult(ex.Result.Stdout, ex.Result.Stderr, ex.Result.ExitCode);
        }
    }
}
