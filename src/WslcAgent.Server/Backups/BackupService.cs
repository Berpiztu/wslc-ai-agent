using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.AgentLog;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Backups;

/// <summary>
/// What a volume's backups and a container's have in common
/// (<see cref="Volumes.VolumeBackups"/>, <see cref="Containers.ContainerBackups"/>):
/// each runs in the background, as a container's Run does: the request
/// returns with the job, the card follows it, its output is read and it can be
/// cancelled. A finished job is listed a few seconds, a failed or cancelled
/// one until it is dismissed; the log of one that ended is read by its id
/// after that. Every backup written is kept in the history
/// (<c>{kind}-backups.json</c>), across restarts, with where it went; where a
/// backup goes is one's own folder, else a folder named after it in the
/// folder of all of them (<c>{kind}-backup-settings.json</c>,
/// <see cref="BackupSettings"/>). Every line of a job goes to the agent's log,
/// after <see cref="LogPrefix"/> (the Logs page's Backups group), and to
/// <c>{kind}-backups.log</c>, which a Logs tab reads.
/// </summary>
/// <param name="kind"><c>volume</c> or <c>container</c>: the names of its files.</param>
/// <param name="changed">What the agent says changed when a job starts or ends (<see cref="ChangeNotice"/>): the volumes, the containers.</param>
public abstract partial class BackupService(string kind, string changed, IWslcRunner wslc, WslcEvents events, AgentLogs agentLogs, string dataDirectory, ILogger logger)
{
    private static readonly TimeSpan KeepDone = TimeSpan.FromSeconds(5);

    /// <summary>How long one step of a job may take: an image saved, a volume packed.</summary>
    protected static readonly TimeSpan StepTimeout = TimeSpan.FromHours(4);

    /// <summary>The jobs that ended kept for their log, the newest.</summary>
    private const int KeptJobs = 100;

    /// <summary>The lines kept, the newest: the oldest go as more come.</summary>
    private const int KeptLogLines = 50_000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly ConcurrentDictionary<string, BackupWork> _jobs = new(StringComparer.Ordinal);
    private readonly string _historyPath = Path.Combine(dataDirectory, $"{kind}-backups.json");
    private readonly string _settingsPath = Path.Combine(dataDirectory, $"{kind}-backup-settings.json");

    /// <summary>
    /// The lines of the jobs, <c>time | name | line</c>, kept apart from the
    /// agent's log as the history is: what a Logs tab reads every second while
    /// it is open, a small file read once and then followed in memory, where
    /// the agent's log is every file it holds read again.
    /// </summary>
    private readonly string _logPath = Path.Combine(dataDirectory, $"{kind}-backups.log");
    private readonly Lock _logGate = new();
    private List<string>? _logLines;

    /// <summary>The history and the settings, each read and written whole.</summary>
    private readonly Lock _filesGate = new();

    /// <summary>What every line of a job begins with in the agent's log, the name after it (<c>backup: hermes-data: INFO: Packing hermes-data.</c>).</summary>
    public abstract string LogPrefix { get; }

    /// <summary>The default folder until Settings → Defaults names another: in the user's own folder, outside Documents, which OneDrive may carry away.</summary>
    protected abstract string FirstDefaultFolder { get; }

    /// <summary>A backup is made of parts chosen one by one (a container's, <see cref="BackupWhat"/>); a volume's is the volume.</summary>
    protected virtual bool InParts => false;

    /// <summary>What a backup of <paramref name="name"/> would do as its settings stand: what it holds, the running containers it stops, whether its settings stop them.</summary>
    public abstract Task<BackupPreview> PreviewAsync(string name, string what, CancellationToken cancellationToken);

    /// <summary>The jobs under way, which an update of the agent waits for.</summary>
    public int InFlight => _jobs.Values.Count(job => job.Snapshot().Running);

    /// <summary>Where backups go: the default folder, the folder of all of them, each one's own.</summary>
    public BackupSettings Settings()
    {
        lock (_filesGate)
        {
            try
            {
                if (File.Exists(_settingsPath) && JsonSerializer.Deserialize<BackupSettings>(File.ReadAllText(_settingsPath), Json) is { DefaultFolder.Length: > 0 } read)
                {
                    return read with { Plans = read.Plans ?? [] };
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.LogWarning("{Kind} backup settings could not be read from {Path}: {Message}", kind, _settingsPath, ex.Message);
            }

            return new BackupSettings(FirstDefaultFolder, []);
        }
    }

    /// <summary>
    /// Saves where backups go and how they are made; every folder a full path
    /// of this machine, every choice one of its own; all of them's and one's
    /// own empty for the level below.
    /// </summary>
    public BackupSettings SaveSettings(BackupSettings settings)
    {
        var saved = new BackupSettings(FullPath(settings.DefaultFolder, "default backup folder"),
            [.. (settings.Plans ?? []).Where(plan => plan.Name.Trim().Length > 0)
                .Select(plan => plan with
                {
                    Name = plan.Name.Trim(),
                    Folder = FullPathOrEmpty(plan.Folder, $"backup folder of {plan.Name}"),
                    Stop = Choice(plan.Stop, BackupChoice.Stops, true, $"stopping of {plan.Name}"),
                    What = Parts(plan.What, $"what a backup of {plan.Name} holds"),
                })],
            FullPathOrEmpty(settings.AllFolder, $"backup folder of every {kind}"),
            Choice(settings.DefaultStop, BackupChoice.Stops, false, "default stopping"),
            Choice(settings.AllStop, BackupChoice.Stops, true, $"stopping of every {kind}"),
            Parts(settings.DefaultWhat, "default of what a backup holds"),
            Parts(settings.AllWhat, $"what a backup of every {kind} holds"));
        lock (_filesGate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(saved, Json));
        }

        logger.LogInformation("{Kind} backup settings saved: default folder {Folder}", kind, saved.DefaultFolder);
        Changed();
        return saved;
    }

    /// <summary>
    /// The jobs a card shows (under way, failed or cancelled until dismissed,
    /// one that ended well a few seconds), the history and the settings. A job
    /// that ended well is kept beyond that, its whole log still read by its id,
    /// the last <see cref="KeptJobs"/> of them.
    /// </summary>
    public BackupList List()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshots = _jobs.Values.Select(job => job.Snapshot()).OrderBy(job => job.StartedAt).ToList();
        foreach (var gone in snapshots.Where(job => job.FinishedAt is not null).SkipLast(KeptJobs))
        {
            _jobs.TryRemove(gone.Id, out _);
        }

        var shown = snapshots.Where(job => job is not { State: "success", FinishedAt: { } finished } || now - finished <= KeepDone).ToList();
        var history = History().Select(entry => entry with { Exists = File.Exists(entry.Path) || Directory.Exists(entry.Path) })
            .OrderByDescending(entry => entry.CreatedAt).ToList();
        return new BackupList(shown, history, Settings());
    }

    public BackupJob Get(string id) =>
        _jobs.TryGetValue(id, out var job) ? job.Snapshot() : throw new KeyNotFoundException("No such backup or restore");

    /// <summary>Stops a job under way: its helper goes, and a backup's half-written files with it; a restore leaves things as far as it got.</summary>
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

            _jobs.TryRemove(new KeyValuePair<string, BackupWork>(id, job));
            Changed();
        }
    }

    /// <summary>
    /// A backup of the history deleted: its file or its folder, when it is
    /// still there, and every line that names it (the one it was made of,
    /// those restored from it). Only what the history names: nothing else of
    /// the machine is deleted here. Refused while a restore reads it.
    /// </summary>
    public void DeleteBackup(string path)
    {
        var backup = WslcArgs.Require(path, "backup");
        if (_jobs.Values.Select(job => job.Snapshot()).Any(job => job.Running && string.Equals(job.File, backup, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"{backup} is being restored; wait for it, or cancel it.");
        }

        lock (_filesGate)
        {
            var history = History();
            var entry = history.FirstOrDefault(entry => string.Equals(entry.Path, backup, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"{backup} is not a backup of the history.");
            if (File.Exists(entry.Path))
            {
                File.Delete(entry.Path);
            }
            else if (Directory.Exists(entry.Path))
            {
                Directory.Delete(entry.Path, recursive: true);
            }

            history.RemoveAll(entry => string.Equals(entry.Path, backup, StringComparison.OrdinalIgnoreCase));
            WriteHistory(history);
        }

        logger.LogInformation("{Kind} backup {Backup} deleted", kind, backup);
        Changed();
    }

    /// <summary>
    /// The lines of one's backups and restores, the oldest first, every one
    /// kept; all of them when <paramref name="name"/> is empty, each line said
    /// by its name, the names in a column.
    /// </summary>
    public IReadOnlyList<string> Log(string? name)
    {
        List<(string Time, string Name, string Line)> lines;
        lock (_logGate)
        {
            lines = [.. LogLines()
                .Select(kept => kept.Split(" | ", 3))
                .Where(parts => parts.Length == 3 && (string.IsNullOrEmpty(name) || parts[1] == name))
                .Select(parts => (parts[0], parts[1], parts[2]))];
        }

        var width = lines.Select(line => line.Name.Length).DefaultIfEmpty(0).Max();
        return [.. lines.Select(line => string.IsNullOrEmpty(name)
            ? $"{line.Time} | {line.Name.PadRight(width)} | {line.Line}"
            : $"{line.Time} | {line.Line}")];
    }

    /// <summary>A job started, unless one of the same name is under way.</summary>
    protected BackupJob Start(string jobKind, string name, string what, string file, Func<BackupWork, CancellationToken, Task> work)
    {
        if (_jobs.Values.Any(other => other.Name == name && other.Snapshot().Running))
        {
            throw new InvalidOperationException($"A backup or restore of {name} is under way; wait for it, or cancel it.");
        }

        var job = new BackupWork(Guid.NewGuid().ToString("N"), jobKind, name, what, file, line => Log(name, line));
        _jobs[job.Id] = job;
        _ = Task.Run(() => RunAsync(job, work));
        Changed();
        return job.Snapshot();
    }

    private async Task RunAsync(BackupWork job, Func<BackupWork, CancellationToken, Task> work)
    {
        var said = BackupWhat.Said(job.What).ToLowerInvariant();
        job.Say(job.Kind == BackupJob.Backup ? $"INFO: Backup of {job.Name} ({said}) to {job.File}." : $"INFO: Restore of {job.Name} ({said}) from {job.File}.");
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
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
        {
            // The command's own words in the log, the step and the reason on the card.
            job.Say($"ERROR: {ex.Message}");
            job.Fail(Explained(job.Snapshot().Status, ex.Message));
        }

        Changed();
    }

    /// <summary>A step's command, said, and what it printed added to the job's output.</summary>
    protected internal async Task StepAsync(BackupWork job, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        job.Say($"$ wslc {string.Join(' ', args)}");
        var result = await wslc.RunAsync(args, StepTimeout, cancellationToken);
        foreach (var line in $"{result.Stdout}\n{result.Stderr}".Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            job.Say(line);
        }
    }

    /// <summary>The agent says what changed, so the lists showing it read it again.</summary>
    protected void Changed() => Notice(changed);

    /// <summary>The agent says <paramref name="what"/> changed: a volume a container's restore made, say.</summary>
    protected void Notice(string what) => events.Publish(new ChangeNotice([what]));

    protected List<BackupEntry> History()
    {
        lock (_filesGate)
        {
            try
            {
                return File.Exists(_historyPath) ? JsonSerializer.Deserialize<List<BackupEntry>>(File.ReadAllText(_historyPath), Json) ?? [] : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.LogWarning("{Kind} backups history could not be read from {Path}: {Message}", kind, _historyPath, ex.Message);
                return [];
            }
        }
    }

    protected void Record(BackupEntry backup)
    {
        lock (_filesGate)
        {
            var history = History();
            history.Add(backup);
            WriteHistory(history);
        }
    }

    /// <summary>
    /// The backup one was restored from, among its backups too: one restored
    /// on a new machine lists what it came from. It keeps the date it was made;
    /// one listed already is left as it is.
    /// </summary>
    protected void RecordRestored(string name, string path, string what, long bytes)
    {
        lock (_filesGate)
        {
            var history = History();
            if (history.Any(entry => entry.Name == name && string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var made = history.FirstOrDefault(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase))?.CreatedAt
                ?? new DateTimeOffset(File.Exists(path) ? File.GetLastWriteTime(path) : Directory.GetLastWriteTime(path));
            history.Add(new BackupEntry(name, path, what, bytes, made, true));
            WriteHistory(history);
        }
    }

    private void WriteHistory(List<BackupEntry> history)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_historyPath)!);
            File.WriteAllText(_historyPath, JsonSerializer.Serialize(history, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("{Kind} backups history could not be written to {Path}: {Message}", kind, _historyPath, ex.Message);
        }
    }

    /// <summary>A line of a job, into the agent's log at the level it says (<c>ERROR:</c>, <c>WARNING:</c>), under its name, and into the kept log.</summary>
    private void Log(string name, string line)
    {
        var level = line.StartsWith("ERROR:", StringComparison.Ordinal) ? LogLevel.Error
            : line.StartsWith("WARNING:", StringComparison.Ordinal) ? LogLevel.Warning
            : LogLevel.Information;
        logger.Log(level, "{Prefix}{Name}: {Line}", LogPrefix, name, line);
        lock (_logGate)
        {
            var kept = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {name} | {line}";
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
                logger.LogWarning("{Kind} backups log could not be written to {Path}: {Message}", kind, _logPath, ex.Message);
            }
        }
    }

    /// <summary>
    /// The lines kept, read from the file the first time; with no file yet,
    /// made once from what the agent's log holds of these backups
    /// (<see cref="LogPrefix"/>), so those made before the file was kept are
    /// not lost. Called within <see cref="_logGate"/>.
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
            logger.LogWarning("{Kind} backups log could not be read from {Path}: {Message}", kind, _logPath, ex.Message);
            _logLines ??= [];
        }

        return _logLines;
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

    [GeneratedRegex(@"^wslc( [a-z]+)+( [0-9a-f]{12,64})?: ")]
    private static partial Regex WslcPrefix();

    /// <summary>A path as <c>sh</c> reads it whole: in single quotes, a quote in it closed, escaped and opened again.</summary>
    protected internal static string Quoted(string path) => $"'{path.Replace("'", @"'\''", StringComparison.Ordinal)}'";

    protected static string FullPathOrEmpty(string? folder, string what) =>
        (folder ?? "").Trim().Length > 0 ? FullPath(folder!, what) : "";

    protected static string FullPath(string folder, string what)
    {
        var path = WslcArgs.Require(folder, what);
        return Path.IsPathFullyQualified(path) ? path : throw new ArgumentException($"The {what}, {path}, is not a full path of a folder of this machine.");
    }

    /// <summary>
    /// What a level's backups hold, its parts written in their order; empty for
    /// the level below (the default's: every part), and always for a volume's.
    /// </summary>
    private string Parts(string? value, string what)
    {
        if (!InParts || (value ?? "").Trim().Length == 0)
        {
            return "";
        }

        return BackupWhat.PartsOf(value) is { Count: > 0 } parts
            ? BackupWhat.Of(parts)
            : throw new ArgumentException($"{value} is not the {what}: any of {string.Join(", ", BackupWhat.OfContainer)}, with commas between.");
    }

    /// <summary>
    /// A setting's choice, one of <paramref name="allowed"/>, or empty for the
    /// level below when there is one (<paramref name="orBelow"/>); the agent's
    /// default, which has none, asks when empty.
    /// </summary>
    protected static string Choice(string? value, IReadOnlyList<string> allowed, bool orBelow, string what)
    {
        var choice = (value ?? "").Trim().ToLowerInvariant();
        if (choice.Length == 0)
        {
            return orBelow ? BackupChoice.Default : BackupChoice.Ask;
        }

        return allowed.Contains(choice) ? choice : throw new ArgumentException($"{value} is not a choice of the {what}: {string.Join(", ", allowed)}.");
    }

    /// <summary>Half a backup is none, and would read as one: what was written of it goes.</summary>
    protected void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("backup {Path} left half-written: {Message}", path, ex.Message);
        }
    }
}
