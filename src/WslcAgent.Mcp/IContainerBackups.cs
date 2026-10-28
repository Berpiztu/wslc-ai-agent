using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>A container's backups and restores, run by the agent in the background. Implemented by the server.</summary>
public interface IContainerBackups
{
    /// <summary>
    /// The parts of the container backed up (any of <see cref="BackupWhat.Config"/>,
    /// <see cref="BackupWhat.Image"/>, <see cref="BackupWhat.Volumes"/>; empty
    /// for its settings') into a folder of its own in a host folder, its own
    /// backup folder when <paramref name="folder"/> is empty; returns at once
    /// with the job. The running containers that use its volumes, it among
    /// them, are stopped and started again when its settings say so, or
    /// <paramref name="stop"/> says the user agreed.
    /// </summary>
    BackupJob StartBackup(string container, string what, string folder, bool stop);

    /// <summary>A backup put back, what it holds: the image loaded, the volumes' contents replaced, the container made again; returns at once with the job. Destructive: callers confirm with the user first.</summary>
    BackupJob StartRestore(string folder, string container);

    /// <summary>The jobs under way or just ended, the history of backups and where they go.</summary>
    BackupList List();
}
