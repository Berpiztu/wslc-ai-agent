using System.Collections.Concurrent;
using System.Diagnostics;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Notifications;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// The backup job: check that the container is stopped, export it
/// to <c>%TEMP%\wslc-export-&lt;container&gt;-&lt;job&gt;.tar</c>, offer the
/// archive until the user says the download finished (or discards it), and
/// sweep archives older than six hours that a closed browser left behind.
/// </summary>
public sealed class ContainerBackups(IWslcRunner wslc, Notifier notifier, ILogger<ContainerBackups> logger) : IContainerBackups
{
    private static readonly TimeSpan ExportTimeout = TimeSpan.FromHours(6);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(6);
    private readonly ConcurrentDictionary<string, Entry> _jobs = new();

    public async Task<BackupJob> StartAsync(string container, CancellationToken cancellationToken = default)
    {
        SweepStaleArchives();
        var reference = WslcArgs.Require(container, "container");
        var id = Guid.NewGuid().ToString("N")[..8];
        var entry = new Entry(id, reference, Path.Combine(Path.GetTempPath(), $"wslc-export-{Safe(reference)}-{id}.tar"));
        _jobs[id] = entry;

        var inspection = ContainerInspection.Parse(await wslc.RunAsync(["container", "inspect", reference, "--format", "json"], cancellationToken: cancellationToken));
        entry.Filename = $"{(inspection.Name.Length > 0 ? inspection.Name : inspection.Id)}-{DateTime.Now:yyyy-MM-dd}.tar";
        if (inspection.IsRunning)
        {
            entry.Fail(BackupStep.Check, $"Container {reference} is running. Stop it before backing it up.");
            return entry.Snapshot();
        }

        entry.Step = BackupStep.Archive;
        _ = ExportAsync(entry);
        return entry.Snapshot();
    }

    public BackupJob Get(string job) => Find(job).Snapshot();

    public int InFlight => _jobs.Values.Count(entry => entry.State is BackupState.Running or BackupState.Saving);

    public string? BeginDownload(string job)
    {
        var entry = Find(job);
        if (entry.State is not (BackupState.Ready or BackupState.Saving) || !File.Exists(entry.Path))
        {
            return null;
        }

        entry.State = BackupState.Saving;
        return entry.Path;
    }

    public async Task CancelAsync(string job)
    {
        var entry = Find(job);
        entry.Cancellation.Cancel();
        if (entry.Export is not null)
        {
            try
            {
                await entry.Export;
            }
            catch (Exception ex) when (ex is WslcException or OperationCanceledException or TimeoutException)
            {
                // The export was interrupted on purpose; nothing to report.
            }
        }

        entry.State = BackupState.Cancelled;
        Delete(entry.Path);
        _jobs.TryRemove(job, out _);
    }

    private async Task ExportAsync(Entry entry)
    {
        var took = Stopwatch.StartNew();
        try
        {
            var export = wslc.RunAsync(["container", "export", entry.Container, "--output", entry.Path], ExportTimeout, entry.Cancellation.Token);
            entry.Export = export;
            var result = await export;
            if (!File.Exists(entry.Path))
            {
                throw new WslcException($"wslc container export produced no archive at {entry.Path}", result);
            }

            entry.Step = BackupStep.Ready;
            entry.State = BackupState.Ready;
            notifier.JobEnded("Backup", entry.Container, null, took.Elapsed, NotificationLink.Containers, CliTraceDescription.Containers);
        }
        catch (OperationCanceledException)
        {
            entry.State = BackupState.Cancelled;
        }
        catch (Exception ex) when (ex is WslcException or TimeoutException or IOException)
        {
            logger.LogError("Backup {Job} of {Container} failed: {Message}", entry.Id, entry.Container, ex.Message);
            entry.Fail(BackupStep.Archive, ex.Message);
            Delete(entry.Path);
            notifier.JobEnded("Backup", entry.Container, ex.Message, took.Elapsed, NotificationLink.Containers, CliTraceDescription.Containers);
        }
    }

    private Entry Find(string job) =>
        _jobs.TryGetValue(job, out var entry) ? entry : throw new KeyNotFoundException($"Backup job {job} does not exist.");

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Windows keeps a file open while it is being downloaded; the sweep removes it later.
        }
    }

    private void SweepStaleArchives()
    {
        var limit = DateTime.UtcNow - StaleAfter;
        foreach (var file in Directory.EnumerateFiles(Path.GetTempPath(), "wslc-export-*.tar"))
        {
            if (File.GetLastWriteTimeUtc(file) < limit)
            {
                Delete(file);
            }
        }
    }

    private static string Safe(string value) => string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));

    private sealed class Entry(string id, string container, string path)
    {
        public string Id { get; } = id;

        public string Container { get; } = container;

        public string Path { get; } = path;

        public string State { get; set; } = BackupState.Running;

        public string Step { get; set; } = BackupStep.Check;

        public string Filename { get; set; } = "";

        public string Error { get; private set; } = "";

        public Task? Export { get; set; }

        public CancellationTokenSource Cancellation { get; } = new();

        public void Fail(string step, string error)
        {
            Step = step;
            State = BackupState.Error;
            Error = error;
        }

        public BackupJob Snapshot() =>
            new(Id, Container, State, Step, Filename, File.Exists(Path) ? new FileInfo(Path).Length : 0, Error);
    }
}
