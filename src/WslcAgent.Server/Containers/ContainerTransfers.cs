using System.Collections.Concurrent;
using System.Globalization;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Notifications;
using WslcAgent.Server.Resources;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// The files on their way in and out of containers, as jobs the agent owns, so
/// the Files view that started one can close: the container's row shows the
/// ring, stops the transfer and says why it failed, which is
/// <see cref="ContainerLaunches"/>'s contract applied to bytes. The request
/// itself is still the client's — a browser has to stay open to finish sending
/// or receiving — but what is known about it lives here, so every client sees
/// the same transfer and not only the one that started it.
/// <para>
/// There is no limit to how many run at once, on one container or on several:
/// a selection of files is one of these each, and the row adds them up into a
/// single ring.
/// </para>
/// <para>
/// A finished transfer is listed two seconds more (by then the folder lists
/// the file), a cancelled one 45 seconds so the row says why, and a failed one
/// until it is dismissed — the launches' times, for the same reason: a row
/// must not outlive the user's interest in it, and a failure must not go
/// unseen.
/// </para>
/// </summary>
public sealed class ContainerTransfers(WslcEvents events, ILogger<ContainerTransfers>? logger = null, ResourceRegistry? registry = null, ICliActivity? activity = null, Notifier? notifier = null)
{
    /// <summary>
    /// What every line this writes to the agent's log begins with, and what
    /// puts it under File transfers on the Logs page (AgentLogs.KindOf).
    /// </summary>
    public const string LogPrefix = "transfer: ";

    private static readonly TimeSpan KeepDone = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan KeepCancelled = TimeSpan.FromSeconds(45);

    /// <summary>
    /// What the head of a queue has to start sending in before its whole batch
    /// is let go. The batch and not the file: if the
    /// client that announced it is gone, it is gone for all of them, and
    /// expiring them one at a time would make everyone else wait a minute per
    /// file to find that out.
    /// </summary>
    private static readonly TimeSpan Turn = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, Transfer> _transfers = new(StringComparer.Ordinal);

    /// <summary>
    /// Every transfer's id in the order it arrived, which is the order of both
    /// queues and the order everything is listed in: the head of a direction is
    /// the first of its own still waiting. One list and not two, so that the
    /// order a client announced its files in is kept whichever way they are
    /// going — and so that a screen shows them in that order too, which a
    /// dictionary's own cannot be trusted to be.
    /// </summary>
    private readonly List<string> _announced = [];

    /// <summary>When each direction's current head was given its turn, for the timeout that ends it.</summary>
    private readonly Dictionary<string, (string Id, DateTimeOffset Since)> _turns = new(StringComparer.Ordinal);

    /// <summary>
    /// How many files each announcement had. The ones already through are
    /// forgotten two seconds later, so nothing else remembers that the two
    /// files left were the tail of a batch of ten — and that is what a screen
    /// has to say before it offers to stop them.
    /// </summary>
    private readonly Dictionary<string, int> _batches = new(StringComparer.Ordinal);

    private readonly Lock _queue = new();

    /// <summary>
    /// Takes note of a file starting to move and hands back the handle the
    /// endpoint reports to. <paramref name="clientGone"/> is the request's own
    /// token: a client that closes while bytes are still travelling takes the
    /// transfer with it, because nobody else can carry them — but only until
    /// the last byte is in. From there the agent is alone with the file and
    /// finishes on its own.
    /// <para>
    /// For a file nobody announced: it is its own batch of one, given the turn
    /// as it starts, so a client that has not learnt to queue still works and
    /// still shows up in everyone's list.
    /// </para>
    /// </summary>
    public Transfer Start(string container, string direction, string directory, string name, long totalBytes, CancellationToken clientGone)
    {
        var transfer = new Transfer(Guid.NewGuid().ToString("N"), container, direction, directory, name, totalBytes, "", Ended);
        Open(transfer);
        _transfers[transfer.Id] = transfer;
        lock (_queue)
        {
            // Its place in the order things arrived, so it is listed among the
            // queued ones where it belongs and not wherever a dictionary puts it.
            _announced.Add(transfer.Id);
        }

        transfer.Claim(clientGone);
        Log(LogLevel.Information, $"{Where(transfer)} started, not queued");
        // The container's rows are stale: one of them has a file on the move.
        events.Publish(new ChangeNotice([ChangeNotice.Container]));
        return transfer;
    }

    /// <summary>
    /// A client says what it is about to move: the files go to the back of
    /// their direction's queue, in the order given, and the ids that come back
    /// are what make them that client's own. Nothing
    /// travels yet — the head of the queue is told its turn has come by the
    /// change stream, and sends then.
    /// </summary>
    public AnnouncedTransfers Announce(string container, string direction, string directory, IReadOnlyList<AnnouncedFile> files)
    {
        if (direction != ContainerTransfer.In && direction != ContainerTransfer.Out)
        {
            throw new ArgumentException($"A transfer goes {ContainerTransfer.In} or {ContainerTransfer.Out}.", nameof(direction));
        }

        if (files.Count == 0 || files.Any(file => file.Name.Trim().Length == 0))
        {
            throw new ArgumentException("A batch is one or more files, each with a name.", nameof(files));
        }

        var batch = Guid.NewGuid().ToString("N");
        var announced = new List<Transfer>();
        lock (_queue)
        {
            _batches[batch] = files.Count;
            foreach (var file in files)
            {
                var transfer = new Transfer(Guid.NewGuid().ToString("N"), container, direction, directory, file.Name, file.Size, batch, Ended);
                Open(transfer);
                _transfers[transfer.Id] = transfer;
                _announced.Add(transfer.Id);
                announced.Add(transfer);
            }
        }

        Log(LogLevel.Information, $"{announced.Count} file(s) announced, {(direction == ContainerTransfer.In ? "going up into" : "coming down from")} {ContainerName(container)}:{directory}: {string.Join(", ", announced.Select(transfer => transfer.Name))} (batch {batch[..8]})");
        events.Publish(new ChangeNotice([ChangeNotice.Container]));
        return new AnnouncedTransfers(batch, [.. announced.Select(transfer => Placed(transfer))]);
    }

    /// <summary>
    /// The head of a queue takes its turn: the request carrying the file says
    /// which one it is, and the transfer starts. Anything but the head is
    /// refused — a file whose turn has not come, one already travelling, and
    /// one the agent has never heard of alike — so a client cannot cut the
    /// line by sending early.
    /// </summary>
    public Transfer Claim(string id, CancellationToken clientGone)
    {
        Transfer claimed;
        lock (_queue)
        {
            Expire();
            if (!_transfers.TryGetValue(id, out var transfer) || !transfer.IsWaiting)
            {
                Log(LogLevel.Warning, $"a file came to be sent that is not waiting in the queue ({(transfer is null ? $"unknown id {id[..Math.Min(8, id.Length)]}" : $"{Where(transfer)}, {transfer.Phase}")}): refused");
                throw new KeyNotFoundException("No such file is waiting in the queue");
            }

            if (Head(transfer.Direction) != id)
            {
                Log(LogLevel.Warning, $"{Where(transfer)} came to be sent before its turn: refused");
                throw new InvalidOperationException("It is not this file's turn yet.");
            }

            transfer.Claim(clientGone);
            _turns.Remove(transfer.Direction);
            claimed = transfer;
            Log(LogLevel.Information, $"{Where(transfer)} started");
        }

        events.Publish(new ChangeNotice([ChangeNotice.Container]));
        return claimed;
    }

    /// <summary>
    /// Lets a whole announcement go: the client saying it will not send those
    /// files after all, and what the timeout does when nobody takes the turn.
    /// Only the ones still waiting — one already travelling is stopped by its
    /// own cross, which is a different thing and says so on the row.
    /// </summary>
    public void DropBatch(string batch)
    {
        if (batch.Length == 0)
        {
            return;
        }

        lock (_queue)
        {
            var dropped = _transfers.Values.Where(t => t.Batch == batch && t.IsWaiting).ToList();
            foreach (var transfer in dropped)
            {
                Forget(transfer, "cancelled", "let go by its client before its turn");
            }

            if (dropped.Count > 0)
            {
                Log(LogLevel.Information, $"batch {batch[..Math.Min(8, batch.Length)]} let go by its client before its turn: {string.Join(", ", dropped.Select(Where))}");
            }
        }

        events.Publish(new ChangeNotice([ChangeNotice.Container]));
    }

    /// <summary>
    /// The id whose turn it is in that direction: the first still waiting, in
    /// the order everything was announced — once nothing of that direction is
    /// travelling. Empty when nothing is waiting, or while a file is on its way.
    /// Called under <see cref="_queue"/>.
    /// <para>
    /// The turn waits for the one travelling. It
    /// was given to the next file the moment the one before it started, so its
    /// minute ran out while the first was still going up: an installer over a
    /// slow link takes longer than that, the whole rest of the batch was let go,
    /// and the client, sending one file after another, found its second gone
    /// and said nothing. And while it had the turn, another client could have
    /// sent beside the one travelling, which is what the queue is for stopping.
    /// </para>
    /// </summary>
    private string Head(string direction) =>
        Travelling(direction) > 0 ? ""
        : _announced.FirstOrDefault(id => _transfers.TryGetValue(id, out var transfer)
            && transfer.Direction == direction && transfer.IsWaiting) ?? "";

    /// <summary>How many files of that direction are on their way: one, as the queue runs. Called under <see cref="_queue"/>.</summary>
    private int Travelling(string direction) =>
        _transfers.Values.Count(transfer => transfer.Direction == direction && transfer.IsTravelling);

    /// <summary>
    /// Gives the turn to the head of each queue and takes it away from a head
    /// that has not used it: its whole batch goes and the next one is up. The
    /// sweep runs whenever anyone reads the list or claims a turn, which is
    /// every second while anything is moving — there is no clock of its own,
    /// because a queue nobody is watching is a queue nobody is waiting in.
    /// Called under <see cref="_queue"/>.
    /// </summary>
    private bool Expire()
    {
        var now = DateTimeOffset.UtcNow;
        var changed = false;
        foreach (var direction in (string[])[ContainerTransfer.In, ContainerTransfer.Out])
        {
            var head = Head(direction);
            if (head.Length == 0)
            {
                _turns.Remove(direction);
                continue;
            }

            if (_turns.TryGetValue(direction, out var turn) && turn.Id == head)
            {
                if (now - turn.Since <= Turn)
                {
                    continue;
                }

                // Its minute went by without a byte: the client that announced
                // it is not there any more, and its whole batch goes with it.
                _turns.Remove(direction);
                var batch = _transfers.TryGetValue(head, out var stale) ? stale.Batch : "";
                var gone = _transfers.Values.Where(t => t.IsWaiting && (t.Id == head || (batch.Length > 0 && t.Batch == batch))).ToList();
                foreach (var transfer in gone)
                {
                    Forget(transfer, "error", $"let go: its turn came and nothing was sent within {Turn.TotalSeconds:0} s");
                }

                // The one thing that drops a file with nobody asking: it has to
                // be in the log, or a file that never arrives leaves no trace.
                Log(LogLevel.Warning, $"let go: its turn came and nothing was sent within {Turn.TotalSeconds:0} s, so the client is taken to be gone: {string.Join(", ", gone.Select(Where))}");

                changed = true;
                continue;
            }

            _turns[direction] = (head, now);
            changed = true;
        }

        return changed;
    }

    /// <summary>A file that never travelled: it leaves the queue and the list, and its row in CLI Activity ends saying why.</summary>
    private void Forget(Transfer transfer, string status, string why)
    {
        Close(transfer, status, "", why);
        _transfers.TryRemove(new KeyValuePair<string, Transfer>(transfer.Id, transfer));
        _announced.Remove(transfer.Id);
    }

    /// <summary>One transfer as everyone reads it: how big its announcement was, and how many of its own queue stand before it.</summary>
    private ContainerTransfer Placed(Transfer transfer)
    {
        var snapshot = transfer.Snapshot() with
        {
            BatchSize = transfer.Batch.Length > 0 && _batches.TryGetValue(transfer.Batch, out var size) ? size : 0,
        };
        if (!transfer.IsWaiting)
        {
            return snapshot;
        }

        // The one travelling stands ahead of every waiting file too: its client
        // is still sending, and a waiting file whose client read a 0 would go
        // beside it.
        var ahead = Travelling(transfer.Direction) + _announced.TakeWhile(id => id != transfer.Id)
            .Count(id => _transfers.TryGetValue(id, out var other) && other.Direction == transfer.Direction && other.IsWaiting);
        return snapshot with { Position = ahead };
    }

    /// <summary>How many files are on their way or waiting their turn: what an update of the agent waits for, as it would cut them.</summary>
    public int InFlight => _transfers.Values.Count(transfer => transfer.IsWaiting || transfer.IsTravelling);

    /// <summary>Every transfer still worth showing: the ones under way, the ones waiting their turn, and the ones that ended recently with what became of them.</summary>
    public IReadOnlyList<ContainerTransfer> List()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, transfer) in _transfers)
        {
            if (transfer.Phase != ContainerTransfer.Failed && transfer.FinishedAt is { } finished
                && now - finished > (transfer.Phase == ContainerTransfer.Done ? KeepDone : KeepCancelled))
            {
                _transfers.TryRemove(new KeyValuePair<string, Transfer>(id, transfer));
                lock (_queue)
                {
                    _announced.Remove(id);
                }
            }
        }

        bool turned;
        List<ContainerTransfer> listed;
        lock (_queue)
        {
            // The queue is swept on the way out: reading it is what moves it.
            turned = Expire();
            // In the order everything arrived, which is the order of the queue:
            // a list that jumped about would say nothing about who goes next,
            // however right each file's own place said it was.
            listed = [.. _announced.Where(_transfers.ContainsKey).Select(id => Placed(_transfers[id]))];
            // An announcement nothing is left of is forgotten with its files.
            foreach (var batch in _batches.Keys.Where(batch => !_transfers.Values.Any(transfer => transfer.Batch == batch)).ToList())
            {
                _batches.Remove(batch);
            }
        }

        if (turned)
        {
            // Someone's turn came, or someone's went: whoever is waiting has a
            // reason to read again without waiting for their own next tick.
            events.Publish(new ChangeNotice([ChangeNotice.Container]));
        }

        return listed;
    }

    /// <summary>
    /// Stops one transfer: the request is abandoned where it is and the staged
    /// bytes go with it. One still waiting its turn never started, so it is
    /// simply let out of the queue — it is the cross of a file the client is
    /// saying it will not send.
    /// </summary>
    public void Cancel(string id)
    {
        if (!_transfers.TryGetValue(id, out var transfer))
        {
            throw new KeyNotFoundException("No such transfer");
        }

        if (transfer.IsWaiting)
        {
            lock (_queue)
            {
                Forget(transfer, "cancelled", "taken out of the queue before its turn");
            }

            Log(LogLevel.Information, $"{Where(transfer)} taken out of the queue before its turn");

            events.Publish(new ChangeNotice([ChangeNotice.Container]));
            return;
        }

        transfer.Cancel();
    }

    /// <summary>
    /// Forgets a transfer that has ended before its time is up: the row's cross
    /// once it failed or was cancelled, for every client at once. One still on
    /// its way is not dismissed (<see cref="Cancel"/> is for that); one that is
    /// not there is already forgotten.
    /// </summary>
    public void Dismiss(string id)
    {
        if (!_transfers.TryGetValue(id, out var transfer))
        {
            return;
        }

        if (transfer.FinishedAt is null)
        {
            throw new InvalidOperationException("The transfer is still on its way; cancel it first.");
        }

        _transfers.TryRemove(new KeyValuePair<string, Transfer>(id, transfer));
    }

    /// <summary>A transfer that ended: a folder has a new file in it, or a row has a ring to lose — and the log says how it ended.</summary>
    private void Ended(Transfer transfer)
    {
        var ended = transfer.Snapshot();
        Close(transfer,
            ended.Phase switch { ContainerTransfer.Done => "success", ContainerTransfer.Cancelled => "cancelled", _ => "error" },
            ended.Phase == ContainerTransfer.Done ? $"{WslcAgent.ApiClient.Bytes.Humanize(ended.TotalBytes)} in {Spent(transfer.Took)}, after {Spent(transfer.Waited)} waiting its turn" : "",
            ended.Error);
        switch (ended.Phase)
        {
            case ContainerTransfer.Done:
                Log(LogLevel.Information, $"{Where(transfer)} done, {WslcAgent.ApiClient.Bytes.Humanize(ended.TotalBytes)} in {Spent(transfer.Took)}");
                notifier?.JobEnded("Transfer", Where(transfer), null, transfer.Took, NotificationLink.Containers, CliTraceDescription.Transfers);
                break;
            case ContainerTransfer.Cancelled:
                Log(LogLevel.Information, $"{Where(transfer)} cancelled");
                break;
            default:
                Log(LogLevel.Warning, $"{Where(transfer)} failed after {Spent(transfer.Took)}: {ended.Error}");
                notifier?.JobEnded("Transfer", Where(transfer), ended.Error, transfer.Took, NotificationLink.Containers, CliTraceDescription.Transfers);
                break;
        }

        events.Publish(new ChangeNotice([ChangeNotice.Container]));
    }

    /// <summary>
    /// Each step of a file's way — announced, started, ended, and let go —
    /// in the agent's log under File transfers, so a file that never
    /// arrives says where it stopped.
    /// </summary>
    private void Log(LogLevel level, string what) => logger?.Log(level, "{Prefix}{What}", LogPrefix, what);

    /// <summary>A file and where it goes, in the order its commands' titles have: "web · Upload · app.msi → /data".</summary>
    private string Where(Transfer transfer) =>
        $"{ContainerName(transfer.Container)} · {(transfer.Direction == ContainerTransfer.In ? "Upload" : "Download")} · {transfer.Name} {(transfer.Direction == ContainerTransfer.In ? "→" : "←")} {transfer.Directory}";

    /// <summary>A time taken, the way a person reads it: tenths of a second under a minute, minutes and seconds past it.</summary>
    /// <summary>
    /// The file's row in CLI Activity, from the moment it is announced: it
    /// runs while the file waits and travels,
    /// and it ends — done, failed, cancelled or let go — with it, so a file
    /// that started and never finished is a row that says so.
    /// </summary>
    private void Open(Transfer transfer)
    {
        if (activity is null)
        {
            return;
        }

        using (CliTitle.Use(Where(transfer)))
        {
            var upload = transfer.Direction == ContainerTransfer.In;
            transfer.Trace = activity.Start(
                [upload ? "upload" : "download", transfer.Name, upload ? "into" : "from", $"{transfer.Container}:{transfer.Directory}"],
                CliTraceDescription.TransferProgram);
        }
    }

    /// <summary>The file's row ends, once: how long since it was announced, and what became of it.</summary>
    private void Close(Transfer transfer, string status, string outcome, string error)
    {
        if (activity is not null && transfer.Trace is { } trace)
        {
            transfer.Trace = null;
            activity.Finish(trace, DateTimeOffset.UtcNow - transfer.AnnouncedAt, null, status, outcome, error);
        }
    }

    internal static string Spent(TimeSpan time) =>
        time.TotalSeconds < 60
            ? $"{time.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s"
            : $"{(int)time.TotalMinutes} min {time.Seconds} s";

    private string ContainerName(string container) => registry?.NameOf(ResourceRegistry.Container, container) ?? container;

    /// <summary>
    /// One transfer while it lasts: what the endpoint reports to, and what
    /// cancelling it pulls. It is <see cref="IDisposable"/> so the endpoint's
    /// <c>using</c> — or, for a download, the stream that carries the file —
    /// ends it however the request ends, instead of leaving a ring turning for
    /// bytes that stopped moving.
    /// </summary>
    public sealed class Transfer : ITransferProgress, IDisposable
    {
        private readonly Lock _gate = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Action<Transfer> _ended;

        /// <summary>The client's own token while its bytes are still travelling; let go of when they are all in.</summary>
        private CancellationTokenRegistration _clientGone;
        private string _status;
        private string _error = "";
        private long _bytes;
        private long _total;
        private DateTimeOffset? _claimedAt;

        internal Transfer(string id, string container, string direction, string directory, string name, long totalBytes, string batch, Action<Transfer> ended)
        {
            Id = id;
            Container = container;
            Direction = direction;
            Directory = directory;
            Name = name;
            Batch = batch;
            _total = Math.Max(totalBytes, 0);
            _ended = ended;
            // It is born in the queue, whether it waits there for a minute or
            // is claimed in the same breath: one shape for both, so the phase
            // is the only thing that says which.
            Phase = ContainerTransfer.Waiting;
            _status = IsUpload ? $"{name} is waiting its turn to go up…" : $"{name} is waiting its turn to come down…";
        }

        public string Id { get; }

        public string Container { get; }

        public string Direction { get; }

        public string Directory { get; }

        public string Name { get; }

        public string Phase { get; private set; }

        /// <summary>The announcement it came in, empty for a file nobody queued; a batch is let go whole.</summary>
        public string Batch { get; }

        /// <summary>Its turn has not come and nothing of it has travelled.</summary>
        public bool IsWaiting => Phase == ContainerTransfer.Waiting;

        /// <summary>Its turn came and it has not ended: a request is carrying it.</summary>
        public bool IsTravelling => !IsWaiting && FinishedAt is null;

        public DateTimeOffset? FinishedAt { get; private set; }

        /// <summary>When a client said it was coming, which is when its row in CLI Activity began.</summary>
        public DateTimeOffset AnnouncedAt { get; } = DateTimeOffset.UtcNow;

        /// <summary>Its row in CLI Activity while it lasts; null once that row has ended.</summary>
        internal string? Trace { get; set; }

        /// <summary>From being announced to its turn coming.</summary>
        public TimeSpan Waited => (_claimedAt ?? DateTimeOffset.UtcNow) - AnnouncedAt;

        /// <summary>From the moment its turn came — the client starting to send, or the copy out starting — to its end; nothing for one that never started.</summary>
        public TimeSpan Took => _claimedAt is { } claimed ? (FinishedAt ?? DateTimeOffset.UtcNow) - claimed : TimeSpan.Zero;

        /// <summary>The token every step of the transfer runs under: cancelling the job cancels it.</summary>
        public CancellationToken Token => _stop.Token;

        private bool IsUpload => Direction == ContainerTransfer.In;

        /// <summary>
        /// Its turn came and a request is carrying it. <paramref name="clientGone"/>
        /// is that request's own token: a client that closes while bytes are
        /// still travelling takes the transfer with it, because nobody else
        /// can carry them.
        /// </summary>
        public void Claim(CancellationToken clientGone)
        {
            _claimedAt = DateTimeOffset.UtcNow;
            _clientGone = clientGone.Register(static state => ((Transfer)state!).Cancel(), this);
            Step(IsUpload ? ContainerTransfer.Receiving : ContainerTransfer.Copying,
                IsUpload ? $"Uploading {Name}…" : $"Copying {Name} out of the container…");
        }

        public void Total(long bytes)
        {
            lock (_gate)
            {
                _total = Math.Max(bytes, 0);
            }
        }

        public void Progress(long bytes)
        {
            lock (_gate)
            {
                if (FinishedAt is null)
                {
                    _bytes = bytes;
                }
            }
        }

        public void Copying()
        {
            // The bytes are the agent's now: a client that closes its page from
            // here on no longer takes the copy down with it, and only the cross
            // on the row does. This is the half of an upload that survives a
            // browser being reloaded.
            _clientGone.Dispose();
            Step(ContainerTransfer.Copying, $"Copying {Name} into the container…");
        }

        public void Sending() => Step(ContainerTransfer.Sending, $"Sending {Name}…");

        public void Finish()
        {
            lock (_gate)
            {
                // Each stage counts its own bytes from zero, so by the time an
                // upload is through the count belongs to the copy into the
                // container, which has none to give. A file that went whole
                // moved all of it, and that is what a finished job should say
                // rather than nothing at all.
                _bytes = _total;
            }

            if (End(ContainerTransfer.Done, IsUpload ? $"{Name} uploaded" : $"{Name} downloaded", ""))
            {
                _ended(this);
            }
        }

        /// <summary>Why it ended: cancelled when it was this job's own cross that stopped it, failed otherwise.</summary>
        public void Fail(string message)
        {
            var cancelled = _stop.IsCancellationRequested;
            var text = message.Trim().Length > 0 ? message.Trim() : "Transfer failed";
            if (End(cancelled ? ContainerTransfer.Cancelled : ContainerTransfer.Failed, cancelled ? "Transfer cancelled" : text, cancelled ? "" : text))
            {
                _ended(this);
            }
        }

        public void Cancel()
        {
            try
            {
                _stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // It ended while the cross was being pressed: there is nothing
                // left to stop, and the row is about to say what became of it.
            }
        }

        /// <summary>The request ended without saying how: whatever it was, the file is not moving any more.</summary>
        public void Dispose()
        {
            Fail("The transfer ended before the file was through");
            _clientGone.Dispose();
            _stop.Dispose();
        }

        public ContainerTransfer Snapshot()
        {
            lock (_gate)
            {
                return new ContainerTransfer(Id, Container, Direction, Directory, Name, Phase, _status, Percentage(), _bytes, _total, _error, Batch);
            }
        }

        /// <summary>
        /// What the ring shows. An upload has one measurable stage, so it is
        /// the whole ring and the copy into the container holds it at the top.
        /// A download has two, and they are halves of it, as a pull's download
        /// and extraction are.
        /// </summary>
        private int Percentage() => Phase switch
        {
            ContainerTransfer.Receiving => Share(0, 100),
            ContainerTransfer.Copying => IsUpload ? 100 : Share(0, 50),
            ContainerTransfer.Sending => Share(50, 50),
            ContainerTransfer.Done => 100,
            // Ended before its time: the ring is drawn whole in the error
            // colour and the number is not shown beside it, so where it
            // stopped is not worth keeping.
            _ => 0,
        };

        private int Share(int from, int span) =>
            _total > 0 ? (int)Math.Clamp(from + (_bytes * span / _total), from, from + span) : from;

        private void Step(string phase, string status)
        {
            lock (_gate)
            {
                if (FinishedAt is null)
                {
                    (Phase, _status, _bytes) = (phase, status, 0);
                }
            }
        }

        private bool End(string phase, string status, string error)
        {
            lock (_gate)
            {
                if (FinishedAt is not null)
                {
                    return false;
                }

                (Phase, _status, _error, FinishedAt) = (phase, status, error, DateTimeOffset.UtcNow);
                return true;
            }
        }
    }
}
