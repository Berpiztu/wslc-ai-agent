using System.ComponentModel;
using ModelContextProtocol.Server;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp.Tools;

/// <summary>
/// Compose files. The reading is the agent's own, the one its screens show,
/// so what the model is told a file would do here is what a person is told:
/// each service as a launch request, and what the file asks for that cannot
/// be given, named rather than dropped.
/// </summary>
[McpServerToolType]
public static class ProjectTools
{
    [McpServerTool(Name = "parse_compose", ReadOnly = true, Idempotent = true)]
    [Description("Read a Compose file (compose.yaml, docker-compose.yml) into what this agent would do with it, without running anything. Give its path on the agent's machine (the file or its folder), or its text as yaml when the file is not there. A folder may hold several Compose files: the one with a standard name is read and the others are named in warnings, and when none has a standard name the answer is an error that lists them, so ask the user which and give that file's own path. Answers the project's name, each service as the launch request run_container takes (in start order, with what it depends on and whether its profiles leave it out), the networks and volumes it needs, and three lists the user has to be told: unsupported (what the file asks for and WSLC has no equivalent for; '(not yet)' marks what only the agent does not send yet), notNeeded (what WSLC gives already) and warnings (a variable that is not set, a file left unread); and variables, the ${NAME} the file asks for, each with the value it was read as and whether anything sets it, which are the file's parameters to ask the user about. Bringing a project up is not available yet: use this to say whether a file would run here and what would be lost.")]
    public static async Task<ComposePlan> ParseCompose(
        IProjectService projects,
        [Description("The Compose file or its folder, on the agent's machine. Empty when the text is given instead.")] string path = "",
        [Description("The file's text, verbatim, when it is not on the agent's machine. Relative paths, build and env_file need a folder: give it as folder, or they are named as not available.")] string yaml = "",
        [Description("Project name; empty takes the file's own name, then its folder's.")] string name = "",
        [Description("Variables for ${NAME}, KEY=value each; they win over the folder's .env.")] string[]? variables = null,
        [Description("Profiles asked for: a service with profiles is left out unless one of them is here.")] string[]? profiles = null,
        [Description("The context: the folder on the agent's machine the file's relative paths, build, .env and env_file start from. Empty takes the folder the file is in; give it with yaml, which has no folder of its own.")] string folder = "",
        CancellationToken cancellationToken = default)
    {
        // The file line by line, and what a screen says above it, are for a screen: the model has the file, and the three lists say it all.
        var plan = await projects.PlanAsync(new ComposePlanRequest(path, yaml, name, variables, profiles, folder), cancellationToken);
        return plan with { Lines = [], Notes = [] };
    }
}
