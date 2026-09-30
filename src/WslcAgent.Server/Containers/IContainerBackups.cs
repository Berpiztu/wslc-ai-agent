using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Containers;

/// <summary>Container backups (<c>wslc container export</c> to a tar), as jobs the UI polls, downloads and finishes.</summary>
public interface IContainerBackups
{
    /// <summary>Checks the container (it must be stopped) and starts the export in the background.</summary>
    Task<BackupJob> StartAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>The job as it stands; <see cref="KeyNotFoundException"/> when unknown.</summary>
    BackupJob Get(string job);

    /// <summary>The archive path of a ready job, which then moves to <c>saving</c>; null while it is not ready.</summary>
    string? BeginDownload(string job);

    /// <summary>Finishes or discards the job: stops a running export and deletes the archive.</summary>
    Task CancelAsync(string job);

    /// <summary>
    /// How many backups are exporting or being downloaded right now: what an
    /// update of the agent waits for. A ready archive nobody has come for is not
    /// one of them: a closed browser leaves it there for hours, and the update
    /// would wait for it that long.
    /// </summary>
    int InFlight { get; }
}
