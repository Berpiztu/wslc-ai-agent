using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.AgentLog;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Volumes;

/// <summary>
/// A volume's backups and restores. A guest volume lives in the session's own
/// virtual disk, with every other, so it is read from inside: a helper of its
/// own (<see cref="FilesHelpers.StartVolumeWorkAsync"/>, <see cref="HelperImage"/>)
/// mounts it, packs it into a <c>.tar.gz</c> and the archive is copied out to
/// the host folder asked for; a restore copies the archive in, checks it whole,
/// empties the volume and unpacks it, owners kept by their numbers. Each runs
/// in the background, as a container's Run does: the request returns with the
/// job, the card follows it, its output is read and it can be cancelled. A
/// finished job is listed a few seconds, a failed or cancelled one until it is
/// dismissed; the log of one that ended is read by its id after that. Every backup written is kept in the history
/// (<c>volume-backups.json</c>), across restarts, with where it went. Where a
/// backup goes is the volume's own folder, else every volume's, else the
/// agent's default (<c>volume-backup-settings.json</c>, <see cref="VolumeBackupSettings"/>).
/// </summary>
public sealed partial class VolumeBackups(IWslcRunner wslc, FilesHelpers helpers, IVolumeService volumes, WslcEvents events, AgentLogs agentLogs, IOptions<WslcOptions> options, ILogger<VolumeBackups> logger) : IVolumeBackups
{
    /// <summary>The helper's image: small, with <c>tar</c> and <c>gzip</c>; pulled the first time it is needed.</summary>
    internal const string HelperImage = "alpine:3.24";

    /// <summary>A backup's extension; the host file picker filters by its last part.</summary>
    internal const string Extension = ".tar.gz";

    /// <summary>Where the archive is packed inside the helper, on its way out.</summary>
    private const string Archive = "/tmp/wslc-volume-backup.tar.gz";

    /// <summary>The volume's sockets, listed in the helper for tar to leave out.</summary>
    private const string Sockets = "/tmp/wslc-volume-sockets";

    /// <summary>
    /// The folder a backup is copied into inside the helper, on its way in:
    /// <c>container cp</c> copies into a folder there, never onto a file that
    /// is not there yet, and the backup keeps its own name in it.
    /// </summary>
    private const string Incoming = "/tmp";

    private static readonly TimeSpan StepTimeout = TimeSpan.FromHours(4);
    private static readonly TimeSpan KeepDone = TimeSpan.FromSeconds(5);

    /// <summary>The jobs that ended kept for their log, the newest.</summary>
    private const int KeptJobs = 100;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly ConcurrentDictionary<string, Job> _jobs = new(StringComparer.Ordinal);
    private readonly string _historyPath = Path.Combine(options.Value.DataDirectory, "volume-backups.json");
    private readonly string _settingsPath = Path.Combine(options.Value.DataDirectory, "volume-backup-settings.json");

    /// <summary>
    /// The backup and restore lines, <c>time | volume | line</c>, kept apart
    /// from the agent's log as the history is (<c>volume-backups.log</c>):
    /// what a volume's Logs reads every second while it is open, a small file
    /// read once and then followed in memory, where the agent's log is every
    /// file it holds read again. The same lines are in the agent's log too,
    /// its Backups group.
    /// </summary>
    private readonly string _logPath = Path.Combine(options.Value.DataDirectory, "volume-backups.log");
    private readonly Lock _logGate = new();
    private List<string>? _logLines;

    /// <summary>The lines kept, the newest: the oldest go as more come.</summary>
    private const int KeptLogLines = 50_000;
    /// <summary>The history and the settings, each read and written whole.</summary>
    private readonly Lock _filesGate = new();

    /// <summary>The default folder until Settings → Defaults names another: in the user's own folder, outside Documents, which OneDrive may carry away.</summary>
    internal static readonly string FirstDefaultFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "WSLC AI Agent", "Volume backups");

    /// <summary>Where backups go: the default folder and each volume's own.</summary>
    public VolumeBackupSettings Settings()
    {
        lock (_filesGate)
        {
            try
            {
                if (File.Exists(_settingsPath) && JsonSerializer.Deserialize<VolumeBackupSettings>(File.ReadAllText(_settingsPath), Json) is { DefaultFolder.Length: > 0 } read)
                {
                    return read with { Volumes = read.Volumes ?? [] };
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.LogWarning("volume backup settings could not be read from {Path}: {Message}", _settingsPath, ex.Message);
            }

            return new VolumeBackupSettings(FirstDefaultFolder, []);
        }
    }

    /// <summary>Saves where backups go; every folder a full path of this machine, every volume's and a volume's own empty for the level below.</summary>
    public VolumeBackupSettings SaveSettings(VolumeBackupSettings settings)
    {
        var saved = new VolumeBackupSettings(FullPath(settings.DefaultFolder, "default backup folder"),
            [.. (settings.Volumes ?? []).Where(plan => plan.Volume.Trim().Length > 0)
                .Select(plan => plan with { Volume = plan.Volume.Trim(), Folder = FullPathOrEmpty(plan.Folder, $"backup folder of {plan.Volume}") })],
            FullPathOrEmpty(settings.VolumesFolder, "backup folder of every volume"));
        lock (_filesGate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(saved, Json));
        }

        logger.LogInformation("volume backup settings saved: default folder {Folder}", saved.DefaultFolder);
        events.Publish(new ChangeNotice([ChangeNotice.Volume]));
        return saved;
    }

    private static string FullPathOrEmpty(string? folder, string what) =>
        (folder ?? "").Trim().Length > 0 ? FullPath(folder!, what) : "";

    private static string FullPath(string folder, string what)
    {
        var path = WslcArgs.Require(folder, what);
        return Path.IsPathFullyQualified(path) ? path : throw new ArgumentException($"The {what}, {path}, is not a full path of a folder of this machine.");
    }

    public VolumeBackupJob StartBackup(string volume, string folder)
    {
        var name = WslcArgs.Require(volume, "volume name");
        var directory = FullPath((folder ?? "").Trim().Length > 0 ? folder! : Settings().FolderOf(name), "backup folder");
        var file = Path.Combine(directory, $"{name}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}{Extension}");
        return Start(VolumeBackupJob.Backup, name, file, (job, ct) => BackupAsync(job, directory, ct));
    }

    public VolumeBackupJob StartRestore(string file, string volume)
    {
        var name = WslcArgs.Require(volume, "volume name");
        var archive = WslcArgs.Require(file, "backup file");
        if (!Path.IsPathFullyQualified(archive) || !File.Exists(archive))
        {
            throw new ArgumentException($"{archive} is not a file of this machine.");
        }

        return Start(VolumeBackupJob.Restore, name, archive, RestoreAsync);
    }

    /// <summary>
    /// The jobs a card shows (under way, failed or cancelled until dismissed,
    /// one that ended well a few seconds) and the history. A job that ended
    /// well is kept beyond that, its whole log still read by its id, the last
    /// <see cref="KeptJobs"/> of them.
    /// </summary>
    public VolumeBackupList List()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshots = _jobs.Values.Select(job => job.Snapshot()).OrderBy(job => job.StartedAt).ToList();
        foreach (var gone in snapshots.Where(job => job.FinishedAt is not null).SkipLast(KeptJobs))
        {
            _jobs.TryRemove(gone.Id, out _);
        }

        var shown = snapshots.Where(job => job is not { State: "success", FinishedAt: { } finished } || now - finished <= KeepDone).ToList();
        var history = History().Select(entry => entry with { Exists = File.Exists(entry.Path) }).OrderByDescending(entry => entry.CreatedAt).ToList();
        return new VolumeBackupList(shown, history, Settings());
    }

    public VolumeBackupJob Get(string id) =>
        _jobs.TryGetValue(id, out var job) ? job.Snapshot() : throw new KeyNotFoundException("No such backup or restore");

    /// <summary>Stops a job under way: its helper goes, and a backup's half-written file with it; a restore leaves the volume as far as it got.</summary>
    public void Cancel(string id)
    {
        if (!_jobs.TryGetValue(id, out var job))
        {
            throw new KeyNotFoundException("No such backup or restore");
        }

        job.Cancel();
    }

    /// <summary>Forgets a job that ended; one still under way is cancelled first.</summary>
    public void Dismiss(string id)
    {
        if (_jobs.TryGetValue(id, out var job))
        {
            if (job.Snapshot().Running)
            {
                throw new InvalidOperationException("It is still under way; cancel it first.");
            }

            _jobs.TryRemove(new KeyValuePair<string, Job>(id, job));
            events.Publish(new ChangeNotice([ChangeNotice.Volume]));
        }
    }

    private VolumeBackupJob Start(string kind, string volume, string file, Func<Job, CancellationToken, Task> work)
    {
        if (_jobs.Values.Any(other => other.Volume == volume && other.Snapshot().Running))
        {
            throw new InvalidOperationException($"A backup or restore of {volume} is under way; wait for it, or cancel it.");
        }

        var job = new Job(Guid.NewGuid().ToString("N"), kind, volume, file, line => Log(volume, line));
        _jobs[job.Id] = job;
        _ = Task.Run(() => RunAsync(job, work));
        events.Publish(new ChangeNotice([ChangeNotice.Volume]));
        return job.Snapshot();
    }

    private async Task RunAsync(Job job, Func<Job, CancellationToken, Task> work)
    {
        job.Say(job.Kind == VolumeBackupJob.Backup ? $"INFO: Backup of {job.Volume} to {job.File}." : $"INFO: Restore of {job.Volume} from {job.File}.");
        try
        {
            await work(job, job.Token);
            job.Succeed();
        }
        // A cancel removes the helper, and what ran in it may fail before the token is seen.
        catch (Exception) when (job.Token.IsCancellationRequested)
        {
            job.Fail("Cancelled", cancelled: true);
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            // The command's own words in the log, the step and the reason on the card.
            job.Say($"ERROR: {ex.Message}");
            job.Fail(Explained(job, ex.Message));
        }

        events.Publish(new ChangeNotice([ChangeNotice.Volume]));
    }

    private async Task BackupAsync(Job job, string directory, CancellationToken cancellationToken)
    {
        await volumes.InspectAsync(job.Volume, cancellationToken);
        Directory.CreateDirectory(directory);
        var helper = await HelperAsync(job, cancellationToken);
        try
        {
            // A socket is a running program's, made again when it starts: tar cannot keep one, and leaving
            // it out with a warning would end the backup in error. Each one left out is said in the log.
            job.Step($"Listing the sockets of {job.Volume}, which a backup leaves out");
            await StepAsync(job, ["exec", helper, "sh", "-c", $"cd {FilesHelpers.VolumeMount} && find . -type s | tee {Sockets}"], cancellationToken);
            job.Step($"Packing {job.Volume}");
            await StepAsync(job, ["exec", helper, "tar", "czf", Archive, "-X", Sockets, "-C", FilesHelpers.VolumeMount, "."], cancellationToken);
            job.Step($"Copying it to {job.File}");
            await StepAsync(job, ["container", "cp", $"{helper}:{Archive}", job.File], cancellationToken);
            var bytes = new FileInfo(job.File).Length;
            Record(new VolumeBackup(job.Volume, job.File, bytes, DateTimeOffset.Now, true));
            job.Say($"INFO: {job.File} written, {bytes:N0} bytes.");
        }
        catch
        {
            // Half a backup is none: it would read as one in the folder.
            TryDelete(job.File);
            throw;
        }
        finally
        {
            await helpers.RemoveHelperAsync(helper);
        }
    }

    private async Task RestoreAsync(Job job, CancellationToken cancellationToken)
    {
        if ((await volumes.ListAsync(cancellationToken)).Volumes.All(volume => volume.Name != job.Volume))
        {
            job.Step($"Creating volume {job.Volume}");
            await volumes.CreateAsync(new CreateVolumeRequest(job.Volume), cancellationToken);
            // Its card comes now, the ring turning on it, not when the restore ends.
            events.Publish(new ChangeNotice([ChangeNotice.Volume]));
        }
        else if ((await volumes.UsersAsync(job.Volume, cancellationToken)).Containers.Where(user => user.State == "running").Select(user => user.Name).ToList() is { Count: > 0 } running)
        {
            throw new InvalidOperationException($"{job.Volume} is in use by {string.Join(", ", running)}: stop {(running.Count == 1 ? "it" : "them")} first, then restore.");
        }

        var helper = await HelperAsync(job, cancellationToken);
        try
        {
            job.Step($"Copying {job.File} in");
            await StepAsync(job, ["container", "cp", job.File, $"{helper}:{Incoming}"], cancellationToken);
            var archive = Quoted($"{Incoming}/{Path.GetFileName(job.File)}");
            // Read whole before anything of the volume goes: a damaged archive leaves it as it was.
            job.Step("Checking the backup");
            await StepAsync(job, ["exec", helper, "sh", "-c", $"tar tzf {archive} > /dev/null"], cancellationToken);
            job.Step($"Emptying {job.Volume} and unpacking the backup into it");
            await StepAsync(job, ["exec", helper, "sh", "-c",
                $"find {FilesHelpers.VolumeMount} -mindepth 1 -delete && tar xzf {archive} --numeric-owner -C {FilesHelpers.VolumeMount}"], cancellationToken);
            job.Say($"INFO: {job.Volume} now holds what {job.File} has.");
            RecordRestored(job.Volume, job.File);
        }
        finally
        {
            await helpers.RemoveHelperAsync(helper);
        }
    }

    /// <summary>
    /// A failure as a person reads it: the step it stopped at and why, without
    /// the command's prefix (<c>wslc exec f2ea7846d42a: </c>); the log keeps
    /// the whole of it.
    /// </summary>
    internal static string Explained(string step, string message)
    {
        var why = WslcPrefix().Replace(message.Trim(), "");
        return $"{step} failed: {(why.Length > 0 ? why : "see the log")}";
    }

    private static string Explained(Job job, string message) => Explained(job.Snapshot().Status, message);

    [GeneratedRegex(@"^wslc( [a-z]+)+( [0-9a-f]{12,64})?: ")]
    private static partial Regex WslcPrefix();

    /// <summary>
    /// What a volume's backups and restores write in the agent's log begins
    /// with, the volume after it (<c>backup: hermes-data: INFO: Packing
    /// hermes-data.</c>): the Logs page's Backups group, and a volume's Logs
    /// tab, read every one of them, kept as the log keeps them.
    /// </summary>
    public const string LogPrefix = "backup: ";

    /// <summary>A line of a job, into the agent's log at the level it says (<c>ERROR:</c>, <c>WARNING:</c>), under its volume.</summary>
    private void Log(string volume, string line)
    {
        var level = line.StartsWith("ERROR:", StringComparison.Ordinal) ? LogLevel.Error
            : line.StartsWith("WARNING:", StringComparison.Ordinal) ? LogLevel.Warning
            : LogLevel.Information;
        logger.Log(level, "{Prefix}{Volume}: {Line}", LogPrefix, volume, line);
        lock (_logGate)
        {
            var kept = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {volume} | {line}";
            LogLines().Add(kept);
            try
            {
                if (_logLines!.Count > KeptLogLines)
                {
                    _logLines.RemoveRange(0, _logLines.Count - KeptLogLines);
                    File.WriteAllLines(_logPath, _logLines);
                }
                else
                {
                    File.AppendAllLines(_logPath, [kept]);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("volume backups log could not be written to {Path}: {Message}", _logPath, ex.Message);
            }
        }
    }

    /// <summary>
    /// A volume's backup and restore lines, the oldest first, every one kept;
    /// every volume's when <paramref name="volume"/> is empty, each line said
    /// by its volume, the names in a column.
    /// </summary>
    public IReadOnlyList<string> Log(string? volume)
    {
        List<(string Time, string Volume, string Line)> lines;
        lock (_logGate)
        {
            lines = [.. LogLines()
                .Select(kept => kept.Split(" | ", 3))
                .Where(parts => parts.Length == 3 && (string.IsNullOrEmpty(volume) || parts[1] == volume))
                .Select(parts => (parts[0], parts[1], parts[2]))];
        }

        var width = lines.Select(line => line.Volume.Length).DefaultIfEmpty(0).Max();
        return [.. lines.Select(line => string.IsNullOrEmpty(volume)
            ? $"{line.Time} | {line.Volume.PadRight(width)} | {line.Line}"
            : $"{line.Time} | {line.Line}")];
    }

    /// <summary>
    /// The lines kept, read from the file the first time; with no file yet,
    /// made once from what the agent's log holds of backups (<see cref="LogPrefix"/>),
    /// so the backups made before the file was kept are not lost. Called
    /// within <see cref="_logGate"/>.
    /// </summary>
    private List<string> LogLines()
    {
        if (_logLines is not null)
        {
            return _logLines;
        }

        try
        {
            if (File.Exists(_logPath))
            {
                return _logLines = [.. File.ReadAllLines(_logPath)];
            }

            _logLines = [.. agentLogs.Messages(LogPrefix)
                .Select(entry => (entry.Timestamp, Parts: entry.Message.Split(": ", 2)))
                .Where(entry => entry.Parts.Length == 2)
                .Select(entry => $"{entry.Timestamp[..19]} | {entry.Parts[0]} | {entry.Parts[1]}")];
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.WriteAllLines(_logPath, _logLines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("volume backups log could not be read from {Path}: {Message}", _logPath, ex.Message);
            _logLines ??= [];
        }

        return _logLines;
    }

    /// <summary>A path as <c>sh</c> reads it whole: in single quotes, a quote in it closed, escaped and opened again.</summary>
    private static string Quoted(string path) => $"'{path.Replace("'", @"'\''", StringComparison.Ordinal)}'";

    /// <summary>The job's helper, its image pulled when this machine does not have it.</summary>
    private async Task<string> HelperAsync(Job job, CancellationToken cancellationToken)
    {
        job.Step($"Starting a helper ({HelperImage}) with {job.Volume} mounted");
        var helper = await helpers.StartVolumeWorkAsync(job.Volume, HelperImage, StepTimeout, cancellationToken);
        job.Cancelling(() => helpers.RemoveHelperAsync(helper));
        return helper;
    }

    /// <summary>A step's command, said, and what it printed added to the job's output.</summary>
    private async Task StepAsync(Job job, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        job.Say($"$ wslc {string.Join(' ', args)}");
        var result = await wslc.RunAsync(args, StepTimeout, cancellationToken);
        foreach (var line in $"{result.Stdout}\n{result.Stderr}".Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            job.Say(line);
        }
    }

    private void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("backup {File} left half-written: {Message}", file, ex.Message);
        }
    }

    private List<VolumeBackup> History()
    {
        lock (_filesGate)
        {
            try
            {
                return File.Exists(_historyPath) ? JsonSerializer.Deserialize<List<VolumeBackup>>(File.ReadAllText(_historyPath), Json) ?? [] : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.LogWarning("volume backups history could not be read from {Path}: {Message}", _historyPath, ex.Message);
                return [];
            }
        }
    }

    private void Record(VolumeBackup backup)
    {
        lock (_filesGate)
        {
            var history = History();
            history.Add(backup);
            WriteHistory(history);
        }
    }

    /// <summary>
    /// The backup a volume was restored from, among that volume's backups too:
    /// a volume restored on a new machine lists what it came from. It keeps
    /// the date it was made; one the volume lists already is left as it is.
    /// </summary>
    private void RecordRestored(string volume, string file)
    {
        lock (_filesGate)
        {
            var history = History();
            if (history.Any(entry => entry.Volume == volume && string.Equals(entry.Path, file, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var made = history.FirstOrDefault(entry => string.Equals(entry.Path, file, StringComparison.OrdinalIgnoreCase))?.CreatedAt
                ?? new DateTimeOffset(File.GetLastWriteTime(file));
            history.Add(new VolumeBackup(volume, file, new FileInfo(file).Length, made, true));
            WriteHistory(history);
        }
    }

    /// <summary>
    /// A backup of the history deleted: its file, when it is still there, and
    /// every line that names it (the volume it was made of, those restored
    /// from it). Only a file the history names: nothing else of the machine is
    /// deleted here. Refused while a restore reads it.
    /// </summary>
    public void DeleteBackup(string path)
    {
        var file = WslcArgs.Require(path, "backup file");
        if (_jobs.Values.Select(job => job.Snapshot()).Any(job => job.Running && string.Equals(job.File, file, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"{file} is being restored; wait for it, or cancel it.");
        }

        lock (_filesGate)
        {
            var history = History();
            var backup = history.FirstOrDefault(entry => string.Equals(entry.Path, file, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"{file} is not a backup of the history.");
            if (File.Exists(backup.Path))
            {
                File.Delete(backup.Path);
            }

            history.RemoveAll(entry => string.Equals(entry.Path, file, StringComparison.OrdinalIgnoreCase));
            WriteHistory(history);
        }

        logger.LogInformation("backup {File} of volume deleted", file);
        events.Publish(new ChangeNotice([ChangeNotice.Volume]));
    }

    private void WriteHistory(List<VolumeBackup> history)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_historyPath)!);
            File.WriteAllText(_historyPath, JsonSerializer.Serialize(history, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("volume backups history could not be written to {Path}: {Message}", _historyPath, ex.Message);
        }
    }

    /// <param name="log">Where each line goes besides the job's own output: the agent's log.</param>
    private sealed class Job(string id, string kind, string volume, string file, Action<string> log)
    {
        private const int MaxLines = 2000;

        private readonly Lock _gate = new();
        private readonly CancellationTokenSource _cancel = new();
        private readonly List<string> _output = [];
        private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
        private Func<Task>? _onCancel;
        private string _state = "running";
        private string _status = "Starting";
        private string _error = "";
        private DateTimeOffset? _finished;

        public string Id { get; } = id;

        public string Kind { get; } = kind;

        public string Volume { get; } = volume;

        public string File { get; } = file;

        public CancellationToken Token => _cancel.Token;

        public void Step(string status)
        {
            lock (_gate)
            {
                _status = status;
            }

            Say($"INFO: {status}.");
        }

        public void Say(string line)
        {
            lock (_gate)
            {
                _output.Add(line);
                if (_output.Count > MaxLines)
                {
                    _output.RemoveRange(0, _output.Count - MaxLines);
                }
            }

            log(line);
        }

        /// <summary>What a cancel does besides stopping the command under way: the helper removed, which stops what runs in it.</summary>
        public void Cancelling(Func<Task> onCancel) => _onCancel = onCancel;

        public void Cancel()
        {
            _cancel.Cancel();
            if (_onCancel is { } onCancel)
            {
                _ = onCancel();
            }
        }

        public void Succeed()
        {
            var said = Kind == VolumeBackupJob.Backup ? "Backed up" : "Restored";
            Say($"INFO: {said}.");
            End("success", said, "");
        }

        public void Fail(string message, bool cancelled = false)
        {
            var text = message.Trim().Length > 0 ? message.Trim() : "Failed";
            Say(cancelled ? "WARNING: cancelled." : $"ERROR: {text}");
            End(cancelled ? "cancelled" : "error", text, cancelled ? "" : text);
        }

        private void End(string state, string status, string error)
        {
            lock (_gate)
            {
                if (_finished is not null)
                {
                    return;
                }

                (_state, _status, _error, _finished) = (state, status, error, DateTimeOffset.UtcNow);
            }
        }

        public VolumeBackupJob Snapshot()
        {
            lock (_gate)
            {
                return new VolumeBackupJob(Id, Volume, Kind, _state, _status, File, _error, [.. _output], _started, _finished);
            }
        }
    }
}
