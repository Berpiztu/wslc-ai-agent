using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// A container's mounts as the Files view reads them: which folder is a mount, which mount a path lives in, and which
/// folders hold one further down — so a folder that is a volume or a folder of
/// the host says so, and a read-only one is not offered verbs that would fail.
/// Destinations are cleaned the way every path of the view is, so one written
/// with a trailing slash still matches.
/// </summary>
public sealed class ContainerMounts
{
    public static readonly ContainerMounts None = new([]);

    /// <summary>Deepest first: the mount a path lives in is the nearest one above it.</summary>
    private readonly IReadOnlyList<MountInfo> _mounts;

    public ContainerMounts(IEnumerable<MountInfo> mounts) =>
        _mounts = [.. mounts
            .Where(mount => mount.Destination.Length > 0)
            .Select(mount => mount with { Destination = ContainerPath.Clean(mount.Destination) })
            .Where(mount => mount.Destination != "/")
            .OrderByDescending(mount => mount.Destination.Length)];

    /// <summary>The mount whose mount point this path is.</summary>
    public MountInfo? At(string path)
    {
        var clean = ContainerPath.Clean(path);
        return _mounts.FirstOrDefault(mount => mount.Destination == clean);
    }

    /// <summary>The mount this path lives in: its own, or the nearest one above it.</summary>
    public MountInfo? Holding(string path)
    {
        var clean = ContainerPath.Clean(path);
        return _mounts.FirstOrDefault(mount => clean == mount.Destination || clean.StartsWith(mount.Destination + "/", StringComparison.Ordinal));
    }

    /// <summary>The mounts further down this folder, nearest first, so it can be found by walking.</summary>
    public IReadOnlyList<MountInfo> Below(string path)
    {
        var clean = ContainerPath.Clean(path);
        var prefix = clean == "/" ? "/" : clean + "/";
        return [.. _mounts.Where(mount => mount.Destination.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(mount => mount.Destination, StringComparer.Ordinal)];
    }

    /// <summary>Nothing can be written here: the path lives in a mount made read-only.</summary>
    public bool IsReadOnly(string path) => Holding(path) is { Mode: "ro" };

    /// <summary>What the mount is, for a label or a tooltip: "Volume open-webui-data · read-write", "Host folder C:\data · read-only".</summary>
    public static string Describe(MountInfo mount)
    {
        var what = mount.Type switch
        {
            "volume" => $"Volume {mount.Source}",
            "bind" => $"Host folder {mount.Source}",
            _ => $"{mount.Type} {mount.Source}".Trim(),
        };
        return $"{what} · {(mount.Mode == "ro" ? "read-only" : "read-write")}";
    }

    /// <summary>
    /// The mount as <c>--volume</c> writes it, source then where it lands, with
    /// a space after the colon so the host's side and the container's do not
    /// run together: <c>C:\data: /workspace</c>, and
    /// <c>webui-data: /app/data (ro)</c> when it is read-only. Read-write is the ordinary
    /// case and the permissions beside it already say so; read-only is the one
    /// thing they do not show — a folder can read drwxrwxrwx on a mount that
    /// refuses every write.
    /// </summary>
    public static string Spec(MountInfo mount)
    {
        var where = mount.Source.Length > 0 ? $"{mount.Source}: {mount.Destination}" : mount.Destination;
        return mount.Mode == "ro" ? $"{where} (ro)" : where;
    }

    /// <summary>A volume is a drive, a host folder a computer, anything else (tmpfs) memory.</summary>
    public static string Icon(MountInfo mount) => mount.Type switch
    {
        "volume" => MudBlazor.Icons.Material.Outlined.Storage,
        "bind" => MudBlazor.Icons.Material.Outlined.Computer,
        _ => MudBlazor.Icons.Material.Outlined.Memory,
    };
}
