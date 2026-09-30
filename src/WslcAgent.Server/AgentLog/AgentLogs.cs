using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.AgentLog;

/// <summary>
/// The Logs page's reading of <see cref="AgentLogFile"/>:
/// every file parsed into entries (a line in the log format starts
/// one, the lines after it are its details), merged by timestamp, the last
/// <c>tail</c> kept; deleting rewrites each file without the chosen entries.
/// A <c>wslc</c> command's entry is read back as the command it records
/// (<see cref="CliTraceBlock"/>), and the commands running at this moment,
/// which are not in any file yet, are listed after the last entry — so the
/// page shows what CLI Activity showed, and the <c>cli_activity</c> tool reads
/// its commands from the same place.
/// <para>
/// A list is abridged: a command's entry carries its outcome line and not its
/// output, which is read whole, by id, when a row is opened
/// (<see cref="Entries"/>). The whole list with every output inside was tens
/// of megabytes, deserialised in the browser every two seconds, and it left
/// the application slow long after the page was closed.
/// </para>
/// </summary>
public sealed partial class AgentLogs(AgentLogFile file, ICliActivity activity) : ICliActivityLog
{
    public const int DefaultTail = 500;
    public const int MaxTail = 5000;

    /// <summary>
    /// An entry's id is its file and its line: the file's ordinal (the order this
    /// process first saw it) times this, plus the line its entry starts on. It
    /// does not move when another process writes an older line in between, as a
    /// position in the merged stream did, so a page can ask for what comes after
    /// the last id it has, and delete by id, and be answered about the same entry.
    /// </summary>
    public const int IdsPerFile = 1 << 22;

    /// <summary>The level a running command is listed under: a state, not a severity, since nothing has been written yet.</summary>
    public const string RunningLevel = "RUNNING";

    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";

    private readonly Dictionary<string, int> _ordinals = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The last <paramref name="tail"/> entries of the files, then the commands
    /// running right now — abridged. With <paramref name="after"/>, only the
    /// entries after that one: what a page that keeps refreshing asks for, so its
    /// list only grows at the end and what is being read never moves; an id that
    /// is no longer there (deleted) answers with the tail.
    /// </summary>
    public IReadOnlyList<AgentLogEntry> Read(int? tail, int? after = null)
    {
        var entries = ReadAll();
        var index = after is { } id ? entries.FindIndex(entry => entry.Id == id) : -1;
        var kept = index >= 0 ? entries.Skip(index + 1).ToList() : Tail(entries, tail);
        return [.. kept.Select(entry => ToContract(entry, whole: false)), .. activity.Running().Select(RunningEntry)];
    }

    /// <summary>The entries with these ids, whole: a command's output entire, never cut.</summary>
    public IReadOnlyList<AgentLogEntry> Entries(IReadOnlyCollection<int> entryIds)
    {
        var ids = entryIds.ToHashSet();
        return ReadAll().Where(entry => ids.Contains(entry.Id)).Select(entry => ToContract(entry, whole: true)).ToList();
    }

    /// <summary>The <c>wslc</c> commands, newest first: the ones running now, then the finished ones the files hold.</summary>
    public IReadOnlyList<CliTraceEntry> Recent(int max)
    {
        var running = Enumerable.Reverse(activity.Running());
        var finished = Enumerable.Reverse(ReadAll()).Select(CommandOf).OfType<CliTraceEntry>();
        return [.. running.Concat(finished).Take(Math.Max(1, max))];
    }

    /// <summary>The same entries as plain text.</summary>
    public string Text(int? tail)
    {
        var kept = Tail(ReadAll(), tail);
        if (kept.Count == 0)
        {
            return "No logs yet.";
        }

        var text = new StringBuilder();
        foreach (var entry in kept)
        {
            text.Append(entry.Timestamp).Append(" | ").Append(entry.Level.PadRight(8)).Append(" | ")
                .Append(entry.Source).Append(" - ").Append(entry.Message).Append('\n');
            foreach (var detail in entry.Details)
            {
                text.Append(detail).Append('\n');
            }
        }

        return text.ToString().TrimEnd('\n');
    }

    /// <summary>Removes the entries with these ids from the files they came from; how many went.</summary>
    public int Delete(IReadOnlyCollection<int> entryIds)
    {
        if (entryIds.Count == 0)
        {
            return 0;
        }

        var ids = entryIds.ToHashSet();
        return Rewrite(entry => !ids.Contains(entry.Id));
    }

    /// <summary>Empties every file: the whole log goes, the entries past the tail included; how many went.</summary>
    public int Clear() => Rewrite(_ => false);

    /// <summary>Rewrites each file with the entries <paramref name="keep"/> keeps; how many were dropped.</summary>
    private int Rewrite(Func<Entry, bool> keep)
    {
        var deleted = 0;
        lock (file.Gate)
        {
            foreach (var group in ReadAll().GroupBy(entry => entry.File))
            {
                var kept = group.Where(keep).ToList();
                var removed = group.Count() - kept.Count;
                if (removed == 0)
                {
                    continue;
                }

                try
                {
                    File.WriteAllText(group.Key, string.Concat(kept.SelectMany(entry => entry.RawLines).Select(line => line + "\n")));
                    deleted += removed;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A file another process still holds keeps its entries; the others are rewritten.
                }
            }
        }

        return deleted;
    }

    internal static int NormalizeTail(int? tail) => tail is null or <= 0 ? DefaultTail : Math.Min(tail.Value, MaxTail);

    /// <summary>Entries of one file, in the order written, each knowing the line it starts on.</summary>
    internal static List<Entry> Parse(IEnumerable<string> lines, string path)
    {
        var entries = new List<Entry>();
        var number = 0;
        foreach (var line in lines)
        {
            var match = EntryLine().Match(line);
            if (match.Success)
            {
                entries.Add(new Entry(path, number, match.Groups["timestamp"].Value, match.Groups["level"].Value,
                    match.Groups["source"].Value.Trim(), match.Groups["message"].Value, [], [line]));
            }
            else if (entries.Count > 0)
            {
                entries[^1].Details.Add(line);
                entries[^1].RawLines.Add(line);
            }

            number++;
        }

        return entries;
    }

    private static List<Entry> Tail(List<Entry> entries, int? tail) =>
        entries.Skip(Math.Max(0, entries.Count - NormalizeTail(tail))).ToList();

    /// <summary>Every file merged into one stream, each entry with its id (<see cref="IdsPerFile"/>).</summary>
    private List<Entry> ReadAll() =>
        file.Files()
            .SelectMany(path => Parse(ReadLines(path), path).Select(entry => entry with { Id = Ordinal(path) * IdsPerFile + entry.Line }))
            .OrderBy(entry => entry.Timestamp, StringComparer.Ordinal)
            .ThenBy(entry => entry.File, StringComparer.Ordinal)
            .ToList();

    /// <summary>A file's number, given the first time this process sees it and kept for as long as the process runs.</summary>
    private int Ordinal(string path)
    {
        lock (_ordinals)
        {
            if (!_ordinals.TryGetValue(path, out var ordinal))
            {
                ordinal = _ordinals.Count;
                _ordinals[path] = ordinal;
            }

            return ordinal;
        }
    }

    /// <summary>A file the writer holds is read through its share mode; a transient lock reads as empty.</summary>
    private static IEnumerable<string> ReadLines(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd().ReplaceLineEndings("\n").TrimEnd('\n');
            return text.Length == 0 ? [] : text.Split('\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The entry as the API hands it. Abridged, a command's entry keeps its
    /// outcome line and drops the output sections and the output itself; any
    /// other entry is small and travels whole either way.
    /// </summary>
    private static AgentLogEntry ToContract(Entry entry, bool whole)
    {
        var command = CommandOf(entry);
        var lines = whole || command is null ? entry.Details : entry.Details.TakeWhile(line => !CliTraceBlock.BeginsOutput(line)).ToList();
        var details = Header(entry.Timestamp.Length > 0 ? entry.Timestamp : "n/a", entry.Level, entry.Source, entry.Message);
        if (lines.Count > 0)
        {
            details.Add("");
            details.AddRange(lines);
        }

        if (!whole && command is not null)
        {
            command = command with { Stdout = "", Stderr = "" };
        }

        return new AgentLogEntry(entry.Id, entry.Timestamp, entry.Timestamp[..^2], LevelOf(entry, command), entry.Source, entry.Message, details,
            command?.Kind ?? KindOf(entry.Message), command);
    }

    /// <summary>
    /// A command that failed is an error, whatever its line says: files
    /// written by older versions say WARNING for one, and the page's Error filter
    /// has to find every failure in the file, not only those written since.
    /// </summary>
    private static string LevelOf(Entry entry, CliTraceEntry? command) =>
        command is { Status: "error" or "timeout" } ? "ERROR" : entry.Level;

    /// <summary>A command not yet in the file, as the row it will become: the same source, the same message, its start for a time.</summary>
    private static AgentLogEntry RunningEntry(CliTraceEntry command)
    {
        var timestamp = command.StartedAt.LocalDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var source = typeof(CliActivity).FullName!;
        return new AgentLogEntry(null, timestamp, timestamp[..^2], RunningLevel, source, command.CommandLine,
            Header(timestamp, RunningLevel, source, command.CommandLine), command.Kind, command);
    }

    private static List<string> Header(string timestamp, string level, string source, string message) =>
        [$"Timestamp: {timestamp}", $"Level: {level}", $"Source: {source}", $"Message: {message}"];

    private static CliTraceEntry? CommandOf(Entry entry) =>
        CliTraceBlock.Read(entry.Message, entry.Details, FinishedAt(entry.Timestamp));

    /// <summary>The entry's time as written: local, to the millisecond.</summary>
    private static DateTimeOffset FinishedAt(string timestamp) =>
        new(DateTime.SpecifyKind(DateTime.ParseExact(timestamp, TimestampFormat, CultureInfo.InvariantCulture), DateTimeKind.Local));

    /// <summary>The resource a logged <c>wslc</c> command line acts on, the group the type chips filter by; a step of a file transfer is a file transfer, and so is a step of the agent's own update, which waits for them; a notification goes with what raised it, as its line names; any other message is general.</summary>
    internal static string KindOf(string message) =>
        Notifications.Notifier.AreaIn(message) is { } area ? area
        : message.StartsWith("wslc ", StringComparison.Ordinal)
            ? CliTraceDescription.Describe(message[5..].Split(' ', StringSplitOptions.RemoveEmptyEntries)).Kind
        : message.StartsWith(CliTraceDescription.TransferProgram + " ", StringComparison.Ordinal)
            || message.StartsWith(Containers.ContainerTransfers.LogPrefix, StringComparison.Ordinal)
            || message.StartsWith(Updates.AgentUpdater.LogPrefix, StringComparison.Ordinal)
            ? CliTraceDescription.Transfers
        : CliTraceDescription.General;

    /// <summary>One parsed entry, the line of its file it starts on, and the raw lines it came from, for a rewrite.</summary>
    internal sealed record Entry(string File, int Line, string Timestamp, string Level, string Source, string Message, List<string> Details, List<string> RawLines)
    {
        public int Id { get; init; }
    }

    [GeneratedRegex(@"^(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \| (?<level>[A-Z]+)\s*\| (?<source>[^-]+) - (?<message>.*)$")]
    private static partial Regex EntryLine();
}
