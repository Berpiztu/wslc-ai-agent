using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// A row of a backups table: a backup of the history, or a backup or restore
/// under way (or failed and not dismissed), which stands above them as a row
/// on its way does on a list page.
/// </summary>
public sealed record BackupRow(string Name, BackupEntry? Backup, BackupJob? Job)
{
    /// <summary>When it was made, or when it started.</summary>
    public DateTimeOffset When => Backup?.CreatedAt ?? Job?.StartedAt ?? default;

    /// <summary>What it holds.</summary>
    public string What => Backup?.What ?? Job?.What ?? "";

    /// <summary>The backup's file or folder, or the one its job writes or reads.</summary>
    public string Path => Backup?.Path ?? Job?.File ?? "";

    /// <summary>The jobs under way or failed first, the newest first, then the backups, the newest first; one that ended well is a backup of the list by then.</summary>
    public static IReadOnlyList<BackupRow> Of(IEnumerable<BackupJob> jobs, IEnumerable<BackupEntry> backups) =>
    [
        .. jobs.Where(job => job.State != "success").OrderByDescending(job => job.StartedAt).Select(job => new BackupRow(job.Name, null, job)),
        .. backups.OrderByDescending(backup => backup.CreatedAt).Select(backup => new BackupRow(backup.Name, backup, null)),
    ];
}
