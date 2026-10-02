using System.Text.RegularExpressions;

namespace WslcAgent.UI.Components;

/// <summary>A piece of a log line and the Logs page's class it is drawn with (<c>wslc-log-time</c>, <c>-level</c>, <c>-source</c>, <c>-message</c>, <c>-sep</c>).</summary>
public readonly record struct LogPart(string Text, string Class);

/// <summary>One rendered log line and the level its text reveals ("" when none).</summary>
/// <param name="Parts">The line cut into time, level, source and message, which together are <see cref="Text"/>; empty when it has neither a time nor a level at its start, and it is drawn whole.</param>
public sealed record LogLine(string Text, string Level, IReadOnlyList<LogPart> Parts)
{
    public bool Matches(string query, string level) =>
        (query.Length == 0 || Text.Contains(query, StringComparison.OrdinalIgnoreCase))
        && (level.Length == 0 || Level == level || (level == "error" && Level == "critical"));
}

/// <summary>
/// The log rendering rules: ANSI stripped, one element per line,
/// the level detected from the text (structured tokens, uvicorn prefixes,
/// traceback cues) so lines take the app Logs palette.
/// </summary>
public static partial class LogLines
{
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
