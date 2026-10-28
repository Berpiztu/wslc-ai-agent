using System.ComponentModel;
using ModelContextProtocol.Server;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp.Tools;

/// <summary>Read and additive tools for volumes, their backups too. Remove, prune and restore are destructive and go through the approval gate.</summary>
[McpServerToolType]
public static class VolumeTools
{
    [McpServerTool(Name = "list_volumes", ReadOnly = true)]
    [Description("List managed WSLC volumes on the selected session: name, driver (guest or vhd), mountpoint, scope, labels and whether a container mounts it. Bind mounts of host folders are not volumes and do not appear.")]
    public static async Task<IReadOnlyList<VolumeSummary>> ListVolumes(IVolumeService volumes, CancellationToken cancellationToken = default) =>
        (await volumes.ListAsync(cancellationToken)).Volumes;

    [McpServerTool(Name = "inspect_volume", ReadOnly = true)]
    [Description("The raw wslc volume inspect JSON of a volume: driver, mountpoint, scope, labels, options.")]
    public static Task<VolumeInspect> InspectVolume(IVolumeService volumes, [Description("Volume name.")] string name, CancellationToken cancellationToken = default) =>
        volumes.InspectAsync(name, cancellationToken);

    [McpServerTool(Name = "volume_containers", ReadOnly = true)]
    [Description("The containers (any state) that mount a volume: name, id, image, state, where it is mounted and whether read-write.")]
    public static Task<VolumeUsers> VolumeContainers(IVolumeService volumes, [Description("Volume name.")] string name, CancellationToken cancellationToken = default) =>
        volumes.UsersAsync(name, cancellationToken);

    [McpServerTool(Name = "remove_volume", Destructive = true)]
    [Description("Remove a volume (wslc volume remove); everything stored in it is lost. Destructive: it asks for the user's approval first, through their client's prompt or a confirm token.")]
    public static async Task<object> RemoveVolume(
        IVolumeService volumes,
        ApprovalGate approvals,
        McpServer? server,
        [Description("Volume name.")] string name,
        [Description("The confirm token from the previous answer, once the user approved.")] string? confirm = null,
        CancellationToken cancellationToken = default)
    {
        if (await approvals.CheckAsync(server, "remove_volume", $"remove volume {name}", name,
                new Dictionary<string, string> { ["volume"] = name },
                confirm, cancellationToken) is { } required)
        {
            return required;
        }

        await volumes.RemoveAsync(name, cancellationToken);
        return $"removed {name}";
    }

    [McpServerTool(Name = "prune_volumes", Destructive = true)]
    [Description("Remove every volume no container uses (wslc volume prune -a); their contents go with them. Destructive and not reversible: it asks for the user's approval first.")]
    public static async Task<object> PruneVolumes(
        IVolumeService volumes,
        ApprovalGate approvals,
        McpServer? server,
        [Description("The confirm token from the previous answer, once the user approved.")] string? confirm = null,
        CancellationToken cancellationToken = default)
    {
        if (await approvals.CheckAsync(server, "prune_volumes", "prune every volume no container uses", "volumes:unused",
                new Dictionary<string, string> { ["scope"] = "every volume no container uses" },
                confirm, cancellationToken) is { } required)
        {
            return required;
        }

        return await volumes.PruneAsync(cancellationToken);
    }

    [McpServerTool(Name = "backup_volume")]
    [Description("Back up a volume, every container that uses it stopped: the agent packs it into <volume>-<date>.tar.gz in its backup folder (its own, else a folder named after it in the folder of every volume; volume_backups says which) or the folder given, in the background, and keeps it in the volume's backup history. Running containers that use it are stopped and started again when it ends if its backup settings say yes, or stop=true says the user agreed; if they say no, or ask and stop is false, the backup is refused while they run. Returns the job at once; volume_backups follows it.")]
    public static BackupJob BackupVolume(
        IVolumeBackups backups,
        [Description("Volume name.")] string name,
        [Description("Empty for the volume's backup folder; else a folder of the agent's machine, by its full path (C:\\Backups), made when it is not there.")] string folder = "",
        [Description("The user agreed to stop the running containers that use it; they are started again when the backup ends.")] bool stop = false) =>
        backups.StartBackup(name, folder, stop);

    [McpServerTool(Name = "volume_backups", ReadOnly = true)]
    [Description("The volume backups and restores under way or just ended (state, step, output), every backup's history, newest first (volume, file, size, when, and whether the file is still there), and where backups go: the default folder and each volume's own.")]
    public static BackupList ListVolumeBackups(IVolumeBackups backups) => backups.List();

    [McpServerTool(Name = "restore_volume", Destructive = true)]
    [Description("Restore a backup (.tar.gz) into a volume, in the background: the volume is created when it is not there, and its contents are replaced by the backup's. Refused while a running container uses it. Destructive: it asks for the user's approval first, through their client's prompt or a confirm token.")]
    public static async Task<object> RestoreVolume(
        IVolumeBackups backups,
        ApprovalGate approvals,
        McpServer? server,
        [Description("The backup, a .tar.gz of the agent's machine, by its full path.")] string file,
        [Description("The volume it goes into.")] string name,
        [Description("The confirm token from the previous answer, once the user approved.")] string? confirm = null,
        CancellationToken cancellationToken = default)
    {
        if (await approvals.CheckAsync(server, "restore_volume", $"restore {file} into volume {name}, replacing what it holds", name,
                new Dictionary<string, string> { ["volume"] = name, ["file"] = file },
                confirm, cancellationToken) is { } required)
        {
            return required;
        }

        return backups.StartRestore(file, name);
    }

    [McpServerTool(Name = "create_volume")]
    [Description("Create a volume (wslc volume create). Driver guest (default) or vhd; size (e.g. 8GB) and fixed apply to vhd only.")]
    public static async Task<string> CreateVolume(
        IVolumeService volumes,
        [Description("Volume name.")] string name,
        [Description("guest or vhd; empty for the default.")] string driver = "",
        [Description("vhd size such as 8GB; empty for the default.")] string size = "",
        [Description("vhd only: allocate the full size now.")] bool @fixed = false,
        [Description("One label as key=value, or empty.")] string label = "",
        [Description("One driver option as key=value, or empty.")] string option = "",
        CancellationToken cancellationToken = default)
    {
        await volumes.CreateAsync(new CreateVolumeRequest(name, driver, size, @fixed, label, option), cancellationToken);
        return $"created volume {name}";
    }
}
