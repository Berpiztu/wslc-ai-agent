using System.ComponentModel;
using ModelContextProtocol.Server;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp.Tools;

/// <summary>
/// A pasted <c>docker run</c> or <c>wslc run</c> line, read and run as the Run
/// form's Fill bar reads it. The parser is the product's own
/// (<see cref="RunCommandLine"/>, shared with the UI), so what the model gets
/// here and what a person gets in the form are the same reading — and a flag
/// with no WSLC equivalent is reported rather than dropped in silence.
/// </summary>
[McpServerToolType]
public static class CommandLineTools
{
    [McpServerTool(Name = "parse_run_command", ReadOnly = true, Idempotent = true)]
    [Description("Read a pasted docker run / wslc run line into the fields run_container takes, without running anything. Answers the launch request, the flags this runtime has no equivalent for (unsupported), whether the line was a docker one, and a one-line summary. Use it before run_command_line to show the user what would happen.")]
    public static RunCommandParse ParseRunCommand(
        [Description("The pasted line, verbatim: several lines and \\ continuations are fine.")] string commandLine) =>
        RunCommandLine.Parse(commandLine);

    [McpServerTool(Name = "run_command_line")]
    [Description("Run a pasted docker run / wslc run line: it is read like the Run form's Fill bar and then started, or only created when the line said create. Answers the launch and the flags with no equivalent here (unsupported), which the user has to be told about. Rejects a line with no image.")]
    public static async Task<object> RunCommandLineTool(
        IContainerService containers,
        [Description("The pasted line, verbatim.")] string commandLine,
        CancellationToken cancellationToken = default)
    {
        var parse = RunCommandLine.Parse(commandLine);
        if (parse.Request is null)
        {
            return new { error = parse.Error, summary = parse.Summary };
        }

        // The line's own verb decides: a create line prepares the container and
        // leaves it stopped, exactly as the Run form does with the same paste.
        var launch = parse.Request.Start
            ? await containers.RunAsync(parse.Request, cancellationToken)
            : await containers.CreateAsync(parse.Request, cancellationToken);
        return new
        {
            launch,
            unsupported = parse.Unsupported,
            fromDocker = parse.FromDocker,
            summary = parse.Summary,
        };
    }
}
