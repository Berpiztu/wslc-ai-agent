using System.ComponentModel;
using ModelContextProtocol.Server;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp.Tools;

/// <summary>Read, lifecycle, launch and backup tools for containers. Kill, remove and restore are destructive: they run only with the user's approval (<see cref="ApprovalGate"/>).</summary>
[McpServerToolType]
public static class ContainerTools
{
    [McpServerTool(Name = "container_stats", ReadOnly = true)]
    [Description("One live sample of a container's resource usage (wslc container stats): cpu percent, memory used and limit, disk read/write, network received/sent, pids.")]
    public static Task<ContainerStats> ContainerStats(
        IContainerService containers,
        [Description("Container name or id.")] string container,
        CancellationToken cancellationToken = default) =>
        containers.StatsAsync(container, cancellationToken);

    [McpServerTool(Name = "exec_in_container", Destructive = true)]
    [Description("Run one non-interactive command inside a running container (wslc exec) and return its output: no TTY, no stdin. The command is split like a shell (ls -la /app); use sh -c \"…\" for pipes or redirection. Destructive: a shell inside a container can change anything in it, so the first call answers with a confirm token and the action to approve; call again with confirm=<token> after the user approved.")]
    public static async Task<object> ExecInContainer(
        IContainerService containers,
        ApprovalGate approvals,
        McpServer? server,
        [Description("Container name or id.")] string container,
        [Description("The command to run, e.g. ls -la /app.")] string command,
        [Description("The confirm token from the previous answer, once the user approved.")] string? confirm = null,
        CancellationToken cancellationToken = default)
    {
        if (await approvals.CheckAsync(server, "exec_in_container", $"run `{command}` in container {container}",
                container, new Dictionary<string, string> { ["container"] = container, ["command"] = command },
                confirm, cancellationToken) is { } required)
        {
            return required;
        }

        return await containers.ExecAsync(container, command, cancellationToken);
    }

    [McpServerTool(Name = "kill_container", Destructive = true)]
    [Description("Kill a running container's process at once (wslc container kill), no grace period. Destructive: the first call answers with a confirm token and the action to approve; call again with confirm=<token> after the user approved.")]
    public static async Task<object> KillContainer(
        IContainerService containers,
        ApprovalGate approvals,
        McpServer? server,
        [Description("Container name or id.")] string container,
        [Description("The confirm token from the previous answer, once the user approved.")] string? confirm = null,
        CancellationToken cancellationToken = default)
    {
        if (await approvals.CheckAsync(server, "kill_container", $"kill container {container}",
                container, new Dictionary<string, string> { ["container"] = container },
                confirm, cancellationToken) is { } required)
        {
            return required;
        }

        await containers.KillAsync(container, cancellationToken);
        return $"killed {container}";
    }

    [McpServerTool(Name = "remove_container", Destructive = true)]
    [Description("Remove a container (wslc container rm); its writable layer is lost, volumes stay. A running container is refused unless force=true (rm --force). Destructive: the first call answers with a confirm token and the action to approve; call again with confirm=<token> after the user approved.")]
    public static async Task<object> RemoveContainer(
        IContainerService containers,
        ApprovalGate approvals,
        McpServer? server,
        [Description("Container name or id.")] string container,
        [Description("true removes the container even while it runs.")] bool force = false,
        [Description("The confirm token from the previous answer, once the user approved.")] string? confirm = null,
        CancellationToken cancellationToken = default)
    {
        if (await approvals.CheckAsync(server, "remove_container", $"remove container {container}{(force ? " (forced, even if running)" : "")}",
                container, new Dictionary<string, string> { ["container"] = container, ["force"] = force ? "true" : "false" },
                confirm, cancellationToken) is { } required)
        {
            return required;
        }

        await containers.RemoveAsync(container, force, cancellationToken);
        return $"removed {container}";
    }

    [McpServerTool(Name = "backup_container")]
    [Description("Back up a container, in the background, into a folder named by its date (2026-10-28_065115) in its backup folder (its own, else a folder named after it in the folder of every container; container_backups says which) or the folder given: what holds any of its parts, with commas between: config (its settings, to make it again as it was), image, volumes (config,image,volumes is all of it); empty for what its backup settings tick. Its volumes need every container that uses them stopped, it among them: they are stopped and started again when it ends if its backup settings say yes, or stop=true says the user agreed; if they say no, or ask and stop is false, the backup is refused while they run. A host folder mounted into it is not in the backup. Returns the job at once; container_backups follows it.")]
    public static Task<BackupJob> BackupContainer(
        IContainerBackups backups,
        [Description("Container name.")] string container,
        [Description("Any of config, image and volumes, with commas between; empty for its backup settings'.")] string what = "",
        [Description("Empty for the container's backup folder; else a folder of the agent's machine, by its full path (C:\\Backups), made when it is not there.")] string folder = "",
        [Description("The user agreed to stop it and the running containers that use its volumes; they are started again when the backup ends.")] bool stop = false) =>
        backups.StartBackupAsync(container, what, folder, stop, CancellationToken.None);

    [McpServerTool(Name = "container_backups", ReadOnly = true)]
    [Description("The container backups and restores under way or just ended (state, step, output), every backup's history, newest first (container, folder, what it holds, size, when, and whether it is still there), and where backups go: the default folder and each container's own.")]
    public static BackupList ListContainerBackups(IContainerBackups backups) => backups.List();

    [McpServerTool(Name = "restore_container", Destructive = true)]
    [Description("Restore a container's backup (a folder with its backup.json), in the background, what it holds: its image loaded, its volumes' contents replaced (created when they are not there), and for a backup with its configuration the container made again, stopped, with its settings, under the name given (the one it had when empty). Refused while the container, or a container using one of its volumes, runs. Destructive: it asks for the user's approval first, through their client's prompt or a confirm token.")]
    public static async Task<object> RestoreContainer(
        IContainerBackups backups,
        ApprovalGate approvals,
        McpServer? server,
        [Description("The backup's folder on the agent's machine, by its full path.")] string folder,
        [Description("The container it goes into; empty for the one the backup was made of.")] string container = "",
        [Description("The confirm token from the previous answer, once the user approved.")] string? confirm = null,
        CancellationToken cancellationToken = default)
    {
        var target = container.Length > 0 ? container : "the container it was made of";
        if (await approvals.CheckAsync(server, "restore_container", $"restore {folder} into {target}, replacing its volumes' contents and making it again", folder,
                new Dictionary<string, string> { ["folder"] = folder, ["container"] = target },
                confirm, cancellationToken) is { } required)
        {
            return required;
        }

        return backups.StartRestore(folder, container);
    }

    [McpServerTool(Name = "list_containers", ReadOnly = true)]
    [Description("List WSLC containers on the selected session. all=true (default) includes stopped containers; all=false lists only running ones. Each row has id, name, image, state, status, ports, networks, restart policy and, for running containers, cpu and memory usage.")]
    public static async Task<IReadOnlyList<ContainerSummary>> ListContainers(
        IContainerService containers,
        [Description("Include stopped containers (default true).")] bool all = true,
        CancellationToken cancellationToken = default) =>
        (await containers.ListAsync(all, cancellationToken: cancellationToken)).Containers;

    [McpServerTool(Name = "inspect_container", ReadOnly = true)]
    [Description("Details of one container: name, image, state, ports, created, mounts, the launch settings it was created with (command, env, volumes, networks, limits, health check), its restart policy, and the raw inspect JSON.")]
    public static Task<ContainerDetails> InspectContainer(
        IContainerService containers,
        [Description("Container name or id.")] string container,
        CancellationToken cancellationToken = default) =>
        containers.DetailsAsync(container, cancellationToken);

    [McpServerTool(Name = "container_logs", ReadOnly = true)]
    [Description("The last lines of a container's logs (wslc container logs --tail N).")]
    public static async Task<string> ContainerLogs(
        IContainerService containers,
        [Description("Container name or id.")] string container,
        [Description("Lines from the end (default 200).")] int tail = 200,
        [Description("Prefix each line with its timestamp.")] bool timestamps = false,
        CancellationToken cancellationToken = default) =>
        (await containers.LogsAsync(container, tail, timestamps, cancellationToken)).Text;

    [McpServerTool(Name = "check_container_launch", ReadOnly = true)]
    [Description("Check launch settings before run_container, create_container or recreate_container, without changing anything: errors that would stop the launch (a name in use, a host port taken, a network that does not exist, a host path that does not exist, a value in the wrong shape) and warnings worth a look (a published container port the command and the variables do not name, a --port the command names that is not published, a variable and the command disagreeing on a port, an image that is not local). Each finding names the form field it is about.")]
    public static async Task<LaunchCheck> CheckContainerLaunch(
        ILaunchChecks checks,
        [Description("The launch settings to check; image is required.")] ContainerLaunchRequest request,
        [Description("When recreating: the container being replaced, whose own name and ports are not conflicts.")] string source = "",
        CancellationToken cancellationToken = default) =>
        await checks.CheckAsync(request, source, cancellationToken);

    [McpServerTool(Name = "run_container")]
    [Description("Create and start a container from an image (wslc container run --detach), pulling the image first when it is not local (minutes). Options mirror wslc run: name, command, ports, volumes, env, network, limits, GPUs (gpus: --gpus all), health check, restart policy.")]
    public static async Task<ContainerCreated> RunContainer(
        IContainerService containers,
        [Description("The launch settings; image is required.")] ContainerLaunchRequest request,
        CancellationToken cancellationToken = default) =>
        await containers.RunAsync(request, cancellationToken);

    [McpServerTool(Name = "create_container")]
    [Description("Create a container without starting it (wslc container create). Same options as run_container.")]
    public static async Task<ContainerCreated> CreateContainer(
        IContainerService containers,
        [Description("The launch settings; image is required.")] ContainerLaunchRequest request,
        CancellationToken cancellationToken = default) =>
        await containers.CreateAsync(request, cancellationToken);

    [McpServerTool(Name = "start_container")]
    [Description("Start a stopped container. Does nothing else: it does not create, pull or remove anything.")]
    public static async Task<string> StartContainer(
        IContainerService containers,
        [Description("Container name or id.")] string container,
        CancellationToken cancellationToken = default)
    {
        await containers.StartAsync(container, cancellationToken);
        return $"started {container}";
    }

    [McpServerTool(Name = "stop_container")]
    [Description("Stop a running container gracefully. The container is kept; use start_container to bring it back.")]
    public static async Task<string> StopContainer(
        IContainerService containers,
        [Description("Container name or id.")] string container,
        CancellationToken cancellationToken = default)
    {
        await containers.StopAsync(container, cancellationToken);
        return $"stopped {container}";
    }

    [McpServerTool(Name = "restart_container")]
    [Description("Restart a container (stop, then start).")]
    public static async Task<string> RestartContainer(
        IContainerService containers,
        [Description("Container name or id.")] string container,
        CancellationToken cancellationToken = default)
    {
        await containers.RestartAsync(container, cancellationToken);
        return $"restarted {container}";
    }

    [McpServerTool(Name = "set_restart_policy")]
    [Description("Set the agent-owned restart policy of a container: no, unless-stopped or always. The agent starts enrolled containers when it starts.")]
    public static Task<RestartPolicyInfo> SetRestartPolicy(
        IContainerService containers,
        [Description("Container name or id.")] string container,
        [Description("no, unless-stopped or always.")] string policy,
        CancellationToken cancellationToken = default) =>
        containers.SetRestartPolicyAsync(container, policy, cancellationToken);
}
