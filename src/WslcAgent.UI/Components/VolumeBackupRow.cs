using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// A row of All's Backups: a backup of the history, or a backup or restore
/// under way (or failed and not dismissed), which stands above them as a row
/// on its way does on a list page.
/// </summary>
public sealed record VolumeBackupRow(string Volume, VolumeBackup? Backup, VolumeBackupJob? Job)
{
    /// <summary>When it was made, or when it started.</summary>
    public DateTimeOffset When => Backup?.CreatedAt ?? Job?.StartedAt ?? default;

    /// <summary>Every volume's: the jobs first, then every backup.</summary>
    public static IReadOnlyList<VolumeBackupRow> Of(VolumeBackupList list) => Of(list.Jobs, list.History);

    /// <summary>The jobs under way or failed first, the newest first, then the backups, the newest first; one that ended well is a backup of the list by then.</summary>
    public static IReadOnlyList<VolumeBackupRow> Of(IEnumerable<VolumeBackupJob> jobs, IEnumerable<VolumeBackup> backups) =>
    [
        .. jobs.Where(job => job.State != "success").OrderByDescending(job => job.StartedAt).Select(job => new VolumeBackupRow(job.Volume, null, job)),
        .. backups.OrderByDescending(backup => backup.CreatedAt).Select(backup => new VolumeBackupRow(backup.Volume, backup, null)),
    ];
}
