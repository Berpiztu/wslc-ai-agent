using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>A project's backups and restores, run by the agent in the background. Implemented by the server.</summary>
public interface IProjectBackups
{
    /// <summary>
    /// The project backed up: what the agent keeps of it (its Compose file) and
    /// each of its containers' backups, box by box as <paramref name="picks"/>
    /// decide (null: its settings), every box they do not name as its
    /// container's own settings say; returns at once with the job. The running
    /// containers that use their volumes are stopped and started again when its
    /// settings say so, or <paramref name="stop"/> says the user agreed.
    /// </summary>
    Task<BackupJob> StartBackupAsync(string project, string folder, bool stop, IReadOnlyDictionary<string, bool>? picks, CancellationToken cancellationToken);

    /// <summary>A project's backup put back: its Compose file when <paramref name="file"/> says so, and each container's backup, those of <paramref name="only"/>, every one when it is null, none when empty. Destructive: callers confirm with the user first.</summary>
    BackupJob StartRestore(string folder, string project, IReadOnlyList<string>? only, bool file);

    /// <summary>The jobs under way or just ended, the history of backups and where they go.</summary>
    BackupList List();
}
