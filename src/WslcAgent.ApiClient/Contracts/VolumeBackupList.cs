namespace WslcAgent.ApiClient.Contracts;

/// <summary>Body of <c>POST /api/v1/volumes/{name}/backup</c>: the host folder the backup is written in.</summary>
/// <param name="Folder">A folder of the agent's machine, by its full path, made when it is not there; empty for the volume's own backup folder (<see cref="VolumeBackupSettings"/>).</param>
public sealed record BackupVolumeRequest(string Folder = "");

/// <summary>
/// Where volume backups go, <c>GET</c> and <c>PUT /api/v1/volume-backups/settings</c>,
/// three levels, each over the one below: a volume's own folder, in its plan;
/// else the folder of every volume, set in the window of every volume's
/// backups; else the agent's default, in Settings → Defaults. A volume with
/// no folder of its own backs up to a folder named after it in the one of
/// every volume.
/// </summary>
/// <param name="DefaultFolder">The agent's default (Settings → Defaults), a folder of its machine by its full path.</param>
/// <param name="Volumes">Each volume's plan, its own folder when it has one.</param>
/// <param name="VolumesFolder">The folder of every volume with none of its own; empty for <paramref name="DefaultFolder"/>.</param>
public sealed record VolumeBackupSettings(string DefaultFolder, IReadOnlyList<VolumeBackupPlan>? Volumes = null, string VolumesFolder = "")
{
    /// <summary>Where a volume with no folder of its own backs up: every volume's folder, else the default.</summary>
    public string FolderOfVolumes => VolumesFolder.Length > 0 ? VolumesFolder : DefaultFolder;

    /// <summary>
    /// Where <paramref name="volume"/>'s backups go: its own folder; else a
    /// folder named after it in every volume's, so the volumes with none of
    /// their own keep their backups apart (<c>…\Volume backups\hermes-data</c>).
    /// The agent's machine is Windows: its separator, whatever client asks.
    /// </summary>
    public string FolderOf(string volume) =>
        (Volumes ?? []).FirstOrDefault(plan => plan.Volume == volume) is { Folder.Length: > 0 } plan
            ? plan.Folder
            : $"{FolderOfVolumes.TrimEnd('\\', '/')}\\{volume}";

    /// <summary>The volume has a folder of its own.</summary>
    public bool HasOwnFolder(string volume) => (Volumes ?? []).Any(plan => plan.Volume == volume && plan.Folder.Length > 0);

    /// <summary>The settings with <paramref name="volume"/>'s own folder set; empty for every volume's again.</summary>
    public VolumeBackupSettings WithFolder(string volume, string folder) =>
        this with { Volumes = [.. (Volumes ?? []).Where(plan => plan.Volume != volume), new VolumeBackupPlan(volume, folder.Trim())] };
}

/// <summary>A volume's backup plan: its own folder; the place its schedule goes when backups are scheduled.</summary>
/// <param name="Folder">A folder of the agent's machine, by its full path; empty for the default.</param>
public sealed record VolumeBackupPlan(string Volume, string Folder = "");

/// <summary>Body of <c>POST /api/v1/volumes/restore</c>: a backup put back into a volume, which is created when it is not there.</summary>
/// <param name="File">The backup, a <c>.tar.gz</c> of the agent's machine, by its full path.</param>
/// <param name="Volume">The volume it goes into; its contents are replaced by the backup's.</param>
public sealed record RestoreVolumeRequest(string File, string Volume);

/// <summary>
/// A backup or a restore of a volume the agent runs: started by a request that
/// returns at once, and followed here until it ends. A finished one stays
/// listed a few seconds, a failed or cancelled one until it is dismissed.
/// </summary>
/// <param name="Kind"><see cref="Backup"/> or <see cref="Restore"/>.</param>
/// <param name="State"><c>running</c>, <c>success</c>, <c>error</c> or <c>cancelled</c>.</param>
/// <param name="Status">The step under way, or how it ended.</param>
/// <param name="File">The backup written, or read.</param>
/// <param name="Output">What each step did and printed, a line each.</param>
public sealed record VolumeBackupJob(
    string Id,
    string Volume,
    string Kind,
    string State,
    string Status,
    string File,
    string Error,
    IReadOnlyList<string> Output,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt)
{
    public const string Backup = "backup";
    public const string Restore = "restore";

    public bool Running => State == "running";

    public bool Failed => State is "error" or "cancelled";
}

/// <summary>A backup the agent wrote of a volume, as its history keeps it.</summary>
/// <param name="Path">The <c>.tar.gz</c> on the agent's machine.</param>
/// <param name="Bytes">Its size when it was written.</param>
/// <param name="Exists">The file is still where it was written: one moved or deleted cannot be restored from here.</param>
public sealed record VolumeBackup(string Volume, string Path, long Bytes, DateTimeOffset CreatedAt, bool Exists);

/// <summary>Answer of <c>GET /api/v1/volume-backups</c>: the backups and restores under way or just ended, every backup's history, the newest first, and where backups go.</summary>
public sealed record VolumeBackupList(IReadOnlyList<VolumeBackupJob> Jobs, IReadOnlyList<VolumeBackup> History, VolumeBackupSettings? Settings = null);
