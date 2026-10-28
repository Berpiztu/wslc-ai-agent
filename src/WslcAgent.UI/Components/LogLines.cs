using System.Globalization;
using System.Text.RegularExpressions;

namespace WslcAgent.UI.Components;

/// <summary>A piece of a log line and the Logs page's class it is drawn with (<c>wslc-log-time</c>, <c>-level</c>, <c>-source</c>, <c>-message</c>, <c>-sep</c>).</summary>
public readonly record struct LogPart(string Text, string Class);

/// <summary>One rendered log line and the level its text reveals ("" when none).</summary>
/// <param name="Parts">The line cut into time, level, source and message, which together are <see cref="Text"/>; empty when it has neither a time nor a level at its start, and it is drawn whole.</param>
public sealed record LogLine(string Text, string Level, IReadOnlyList<LogPart> Parts)
{
    public bool Matches(string query) =>
        query.Length == 0 || Text.Contains(query, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The log rendering rules: ANSI stripped, one element per line,
/// the level detected from the text (structured tokens, uvicorn prefixes,
/// traceback cues) so lines take the app Logs palette.
/// </summary>
public static partial class LogLines
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";

    /// <summary>The level toggle a line's level falls under: critical with errors, debug with information; null for a line with no level.</summary>
    public static string? Group(string level) => level switch
    {
        "critical" or "error" => Error,
        "warning" => Warning,
        "info" or "debug" => Info,
        _ => null,
    };

    /// <summary>What a container wrote, in a log put together from several (<see cref="Entry"/>).</summary>
    public const string Output = "output";

    /// <summary>A line of a backup or a restore, in a log put together from several (<see cref="Entry"/>).</summary>
    public const string Backup = "backup";

    /// <summary>The width of the kind column, the longer of <see cref="Output"/> and <see cref="Backup"/>.</summary>
    private static readonly int KindWidth = Math.Max(Output.Length, Backup.Length);

    /// <summary>The width of the level column: CRITICAL, the longest level.</summary>
    private const int LevelWidth = 8;

    /// <summary>One line as it stands, cut as <see cref="Split"/> cuts each of its lines.</summary>
    public static LogLine Of(string line)
    {
        var plain = StripAnsi(line).TrimEnd('\r');
        return new LogLine(plain, DetectLevel(plain), PartsOf(plain));
    }

    /// <summary>
    /// An entry of a log put together from several (a project's containers and
    /// its backups, a container's backups among its own lines), drawn as the
    /// Logs page draws its entries, each part its colour: the moment it was
    /// written on this machine's clock, its level, whose it is (a column of
    /// <paramref name="sourceWidth"/>, none at 0), its kind
    /// (<see cref="Output"/>, <see cref="Backup"/>) and its message, as it was
    /// written but for a level it begins with (<c>INFO:</c>), which the level
    /// column already says.
    /// </summary>
    public static LogLine Entry(TimedLine line, int sourceWidth)
    {
        var message = StripAnsi(line.Text).TrimEnd('\r');
        var level = DetectLevel(message);
        var prefix = Prefix().Match(message);
        if (prefix.Success)
        {
            message = message[prefix.Length..].TrimStart();
        }

        var time = line.At == DateTimeOffset.MinValue
            ? new string(' ', "yyyy-MM-dd HH:mm:ss".Length)
            : line.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var separator = new LogPart(" | ", "wslc-log-sep");
        List<LogPart> parts = [new(time, "wslc-log-time"), separator, new(level.ToUpperInvariant().PadRight(LevelWidth), "wslc-log-level"), separator];
        if (sourceWidth > 0)
        {
            parts.Add(new(line.Source.PadRight(sourceWidth), "wslc-log-source"));
            parts.Add(separator);
        }

        parts.Add(new(line.Kind.PadRight(KindWidth), "wslc-log-source"));
        parts.Add(separator);
        parts.Add(new(message, "wslc-log-message"));
        return new LogLine(string.Concat(parts.Select(part => part.Text)), level, parts);
    }

    public static List<LogLine> Split(string text)
    {
        var plain = StripAnsi(text).Replace("\r\n", "\n").Replace('\r', '\n');
        if (plain.Trim().Length == 0)
        {
            return [];
        }

        var lines = plain.Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines.Select(line => new LogLine(line, DetectLevel(line), PartsOf(line))).ToList();
    }

    /// <summary>
    /// The line as the Logs page draws an entry: its time, its level, its source
    /// (<c>module:function:line</c>, as loguru writes it) and the message, each its colour.
    /// Only a line that starts with a time or a level is cut; any other (a traceback,
    /// plain output) stays whole.
    /// </summary>
    public static IReadOnlyList<LogPart> PartsOf(string line)
    {
        var match = Structured().Match(line);
        if (!match.Success || (match.Groups["time"].Length == 0 && match.Groups["level"].Length == 0))
        {
            return [];
        }

        var parts = new List<LogPart>();
        void Add(string group, string css)
        {
            if (match.Groups[group].Length > 0)
            {
                parts.Add(new LogPart(match.Groups[group].Value, css));
            }
        }

        Add("time", "wslc-log-time");
        Add("sep1", "wslc-log-sep");
        Add("level", "wslc-log-level");
        Add("sep2", "wslc-log-sep");
        Add("source", "wslc-log-source");
        Add("message", "wslc-log-message");
        return parts;
    }

    public static string StripAnsi(string text) => Ansi().Replace(text, "");

    public static string DetectLevel(string line)
    {
        if (line.Trim().Length == 0)
        {
            return "";
        }

        if (Token("CRITICAL").IsMatch(line) || Regex.IsMatch(line, @"\bFATAL\b", RegexOptions.IgnoreCase))
        {
            return "critical";
        }

        if (Token("ERROR").IsMatch(line) || Regex.IsMatch(line, @"\blevel\s*=\s*error\b", RegexOptions.IgnoreCase))
        {
            return "error";
        }

        if (Token("WARN(?:ING)?").IsMatch(line) || Regex.IsMatch(line, @"\blevel\s*=\s*warn(?:ing)?\b", RegexOptions.IgnoreCase))
        {
            return "warning";
        }

        if (Token("DEBUG").IsMatch(line) || Regex.IsMatch(line, @"\bTRACE\b", RegexOptions.IgnoreCase))
        {
            return "debug";
        }

        if (Token("INFO").IsMatch(line) || Regex.IsMatch(line, @"\blevel\s*=\s*info\b", RegexOptions.IgnoreCase))
        {
            return "info";
        }

        var prefix = Prefix().Match(line);
        if (prefix.Success)
        {
            return prefix.Groups[1].Value.ToUpperInvariant() switch
            {
                "CRITICAL" or "FATAL" => "critical",
                "ERROR" => "error",
                "WARNING" or "WARN" => "warning",
                "DEBUG" or "TRACE" => "debug",
                _ => "info",
            };
        }

        return Traceback().IsMatch(line) ? "error" : "";
    }

    private static Regex Token(string name) =>
        new($@"(?:^|[\s|\[]){name}(?:[\s|:\]]|$)", RegexOptions.IgnoreCase);

    [GeneratedRegex(@"\u001b\[[0-?]*[ -/]*[@-~]|\u001b\][^\u0007]*(?:\u0007|\u001b\\)|\u001b[@-Z\\-_]")]
    private static partial Regex Ansi();

    [GeneratedRegex(@"^\s*(CRITICAL|FATAL|ERROR|WARNING|WARN|INFO|DEBUG|TRACE)\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex Prefix();

    /// <summary>
    /// <c>[time] [|] [LEVEL] [|] [module:function:line] message</c>: loguru
    /// (<c>2026-10-02 20:19:47 | INFO | app.x:f:12 - …</c>), Python logging, uvicorn
    /// (<c>INFO:     …</c>), nginx (<c>2026/10/02 13:21:30 [notice] …</c>), an RFC 3339 stamp.
    /// The groups follow one another, so together they are the whole line.
    /// </summary>
    [GeneratedRegex(@"^(?<time>\s*\[?\d{4}[-/]\d{2}[-/]\d{2}[ T]\d{2}:\d{2}:\d{2}(?:[.,]\d+)?(?:Z|[+-]\d{2}:?\d{2})?\]?)?(?<sep1>\s*\|?\s*)(?<level>\[?(?:CRITICAL|FATAL|ERROR|WARNING|WARN|NOTICE|INFO|DEBUG|TRACE)\]?:?(?=[\s|]|$))?(?<sep2>\s*\|?\s*)(?<source>[\w.<>-]+:[\w<>.-]+:\d+(?=\s+-\s))?(?<message>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex Structured();

    [GeneratedRegex(@"^\s*Traceback \(most recent call last\):|^\s*File "".*"", line \d+|^\s*(?:Exception|Error|TypeError|ValueError|RuntimeError)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Traceback();
}
