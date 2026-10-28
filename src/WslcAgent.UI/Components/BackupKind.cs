using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// What tells a volume's backups from a container's; everything else is one
/// component for both (<see cref="BackupFlow"/>, the window, its tables, the
/// ring and the log).
/// </summary>
/// <param name="Key">The agent's name for the kind, its API's <c>/api/v1/{Key}-backups</c>.</param>
/// <param name="Noun">What a backup is of, as a sentence says it.</param>
/// <param name="Icon">Its section's icon in the navigation, beside each one in the window's list.</param>
/// <param name="Change">What the agent says changed when one of them does (<see cref="ChangeNotice"/>).</param>
/// <param name="InParts">A backup holds the parts ticked (a container's, <see cref="BackupWhat"/>), asked when one is made; a volume's is the volume.</param>
/// <param name="InFolders">A backup is a folder (a container's), not a file (a volume's <c>.tar.gz</c>).</param>
/// <param name="Names">The ones on the machine, by name.</param>
/// <param name="Backup">A backup started: of the name, holding the parts chosen, the running containers it needs stopped stopped when the user agreed.</param>
/// <param name="Restore">A restore started: from the backup at the path, into the name.</param>
public sealed record BackupKind(
    string Key,
    string Noun,
    string Icon,
    string Change,
    bool InParts,
    bool InFolders,
    Func<WslcAgentApi, Task<IReadOnlyList<string>>> Names,
    Func<WslcAgentApi, string, string, bool, Task<BackupJob>> Backup,
    Func<WslcAgentApi, string, string, Task<BackupJob>> Restore)
{
    public static readonly BackupKind Volume = new(
        "volume", "volume", MudBlazor.Icons.Material.Filled.Storage, ChangeNotice.Volume, false, false,
        async api => [.. (await api.GetVolumesAsync()).Volumes.Select(volume => volume.Name)],
        (api, name, _, stop) => api.BackupVolumeAsync(name, stop),
        (api, path, name) => api.RestoreVolumeAsync(path, name));

    public static readonly BackupKind Container = new(
        "container", "container", MudBlazor.Icons.Material.Filled.ViewInAr, ChangeNotice.Container, true, true,
        async api => [.. (await api.GetContainersAsync(all: true)).Containers.Select(container => container.Name)],
        (api, name, what, stop) => api.BackupContainerAsync(name, what, stop),
        (api, path, name) => api.RestoreContainerAsync(path, name));

    /// <summary>The noun at the start of a title: <c>Volume</c>, <c>Container</c>.</summary>
    public string Title => char.ToUpperInvariant(Noun[0]) + Noun[1..];

    /// <summary>What a restore does to one that is there, said in red before it runs.</summary>
    public string Replaces(string name) => InFolders
        ? $"What the backup holds replaces what {name} has: its image, its volumes' contents, and, with its configuration, {name} itself, made again with its settings."
        : $"Everything {name} holds now is replaced by the backup's contents.";
}
