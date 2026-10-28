using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Backups;

/// <summary>
/// One backup or restore under way, of a volume or of a container: its steps,
/// what each did and printed, a line each (written to the agent's log as it
/// is said), how it ended, and its cancel.
/// </summary>
/// <param name="log">Where each line goes besides the job's own output: the agent's log and the backups' own.</param>
public sealed class BackupWork(string id, string kind, string name, string what, string file, Action<string> log)
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

    /// <summary><see cref="BackupJob.Backup"/> or <see cref="BackupJob.Restore"/>.</summary>
    public string Kind { get; } = kind;

    /// <summary>The volume or the container.</summary>
    public string Name { get; } = name;

    public string What { get; } = what;

    /// <summary>The backup written, or read.</summary>
    public string File { get; } = file;

    public CancellationToken Token => _cancel.Token;

    /// <summary>A step begun: the status the card shows, and a line of the log.</summary>
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

    /// <summary>What a cancel does besides stopping the command under way: a helper removed, which stops what runs in it.</summary>
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
        var said = Kind == BackupJob.Backup ? "Backed up" : "Restored";
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

    public BackupJob Snapshot()
    {
        lock (_gate)
        {
            return new BackupJob(Id, Name, Kind, What, _state, _status, File, _error, [.. _output], _started, _finished);
        }
    }
}
