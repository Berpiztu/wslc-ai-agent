using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>A volume's backups and restores, run by the agent in the background. Implemented by the server.</summary>
public interface IVolumeBackups
{
    /// <summary>The volume packed into a <c>.tar.gz</c> in a host folder, its own backup folder when <paramref name="folder"/> is empty; returns at once with the job.</summary>
    VolumeBackupJob StartBackup(string volume, string folder);

    /// <summary>A backup put back into a volume, created when it is not there, its contents replaced; returns at once with the job. Destructive: callers confirm with the user first.</summary>
    VolumeBackupJob StartRestore(string file, string volume);

    /// <summary>The jobs under way or just ended, and the history of backups.</summary>
    VolumeBackupList List();
}
