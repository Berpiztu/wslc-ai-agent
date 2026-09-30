using System.ComponentModel;
using ModelContextProtocol.Server;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp.Tools;

/// <summary>Read and additive tools for volumes. Remove and prune are destructive and come with the gated tools.</summary>
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
