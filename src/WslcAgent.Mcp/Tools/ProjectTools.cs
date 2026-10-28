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

    [McpServerTool(Name = "backup_project")]
    [Description("Back up a project, in the background: a folder <project>-<date> in its backup folder (project_backups says which) with the Compose file the agent keeps of it, and each of its containers' backup, its configuration, image and each volume as the project's backup settings tick them box by box, else as that container's own settings say, in the backup's own folder and each container's history, so a container's backups are in one place whether made from the project or alone. Its containers' volumes need every container that uses them stopped: pass stop=true only when the user agreed to stop them; they are started again when it ends. Returns the job at once; project_backups follows it.")]
    public static Task<BackupJob> BackupProject(
        IProjectBackups backups,
        [Description("Project name.")] string project,
        [Description("Empty for the project's backup folder; else a folder of the agent's machine, by its full path (C:\\Backups), made when it is not there.")] string folder = "",
        [Description("The user agreed to stop the running containers that use its containers' volumes; they are started again when the backup ends.")] bool stop = false) =>
        backups.StartBackupAsync(project, folder, stop, null, CancellationToken.None);

    [McpServerTool(Name = "project_backups", ReadOnly = true)]
    [Description("The project backups and restores under way or just ended (state, step, output), every project backup's history, newest first (project, folder, what it holds, size, when, and whether it is still there), and where backups go: the default folder and each project's own, and whether its containers keep theirs under it.")]
    public static BackupList ListProjectBackups(IProjectBackups backups) => backups.List();

    [McpServerTool(Name = "restore_project", Destructive = true)]
    [Description("Restore a project's backup (a folder with its project-backup.json), in the background: the Compose file the agent keeps of it, and each of its containers' backups, as restore_container puts one back (image loaded, volumes replaced, the container made again, stopped). Refused while one of its containers runs. Destructive: it asks for the user's approval first, through their client's prompt or a confirm token.")]
    public static async Task<object> RestoreProject(
        IProjectBackups backups,
        ApprovalGate approvals,
        McpServer? server,
        [Description("The backup's folder on the agent's machine, by its full path.")] string folder,
        [Description("The project it goes into; empty for the one the backup was made of.")] string project = "",
        [Description("The confirm token from the previous answer, once the user approved.")] string? confirm = null,
        CancellationToken cancellationToken = default)
    {
        var target = project.Length > 0 ? project : "the project it was made of";
        if (await approvals.CheckAsync(server, "restore_project", $"restore {folder} into {target}, replacing its containers' volumes and making them again", folder,
                new Dictionary<string, string> { ["folder"] = folder, ["project"] = target },
                confirm, cancellationToken) is { } required)
        {
            return required;
        }

        return backups.StartRestore(folder, project, null, true);
    }
}
