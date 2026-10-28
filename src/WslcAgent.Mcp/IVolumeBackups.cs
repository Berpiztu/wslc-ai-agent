using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>A volume's backups and restores, run by the agent in the background. Implemented by the server.</summary>
public interface IVolumeBackups
{
    /// <summary>
    /// The volume packed into a <c>.tar.gz</c> in a host folder, its own backup
    /// folder when <paramref name="folder"/> is empty; returns at once with the
    /// job. The running containers that use it are stopped and started again
    /// when its settings say so, or <paramref name="stop"/> says the user agreed.
    /// </summary>
    BackupJob StartBackup(string volume, string folder, bool stop);

    /// <summary>A backup put back into a volume, created when it is not there, its contents replaced; returns at once with the job. Destructive: callers confirm with the user first.</summary>
    BackupJob StartRestore(string file, string volume);

    /// <summary>The jobs under way or just ended, the history of backups and where they go.</summary>
    BackupList List();
}
