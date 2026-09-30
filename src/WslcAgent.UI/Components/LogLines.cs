using System.Text.RegularExpressions;

namespace WslcAgent.UI.Components;

/// <summary>One rendered log line and the level its text reveals ("" when none).</summary>
public sealed record LogLine(string Text, string Level)
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

        return lines.Select(line => new LogLine(line, DetectLevel(line))).ToList();
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

    [GeneratedRegex(@"^\s*Traceback \(most recent call last\):|^\s*File "".*"", line \d+|^\s*(?:Exception|Error|TypeError|ValueError|RuntimeError)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Traceback();
}
