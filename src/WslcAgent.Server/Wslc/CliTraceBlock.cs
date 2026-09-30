using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Wslc;

/// <summary>
/// How a finished <c>wslc</c> command is written under its line in the agent's
/// log, and read back from there. One line carries the trace id, the status,
/// the exit code, the duration and, for a command run under a name of its own
/// (<see cref="CliTitle"/>), that name; what the command printed follows under
/// <c>Output:</c> and <c>Errors:</c>, whole — never cut: a
/// JSON cut short is no longer JSON, cannot be laid out and cannot be pasted
/// anywhere. The file is the record of every command the agent ran; the id
/// is what lets the Logs page see a command that was running turn into the
/// entry that says how it ended.
/// </summary>
public static partial class CliTraceBlock
{
    private const string OutputMarker = "Output:";
    private const string ErrorsMarker = "Errors:";

    /// <summary>Whether this detail line opens an output section: where an abridged entry stops.</summary>
    internal static bool BeginsOutput(string line) => line is OutputMarker or ErrorsMarker;

    /// <summary>The detail lines of a finished command, ready to go under its <c>wslc …</c> line.</summary>
    public static string Write(string id, string status, int? exitCode, TimeSpan duration, string stdout, string stderr, string? title = null)
    {
        var text = new StringBuilder()
            .Append("Trace: ").Append(id)
            .Append(" | Status: ").Append(status)
            .Append(" | Exit code: ").Append(exitCode?.ToString(CultureInfo.InvariantCulture) ?? "none")
            .Append(" | Duration: ").Append(((long)duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)).Append(" ms");
        if (!string.IsNullOrWhiteSpace(title))
        {
            // Last on the line, so the lines written before it read as they always did.
            text.Append(" | Title: ").Append(title.ReplaceLineEndings(" "));
        }

        Section(text, OutputMarker, stdout);
        Section(text, ErrorsMarker, stderr);
        return text.ToString();
    }

    /// <summary>
    /// The command a log entry records, when its message is a <c>wslc</c> line
    /// and its detail lines are this block; null for any other entry. The entry
    /// is written when the command ends, so its start is that time less the duration.
    /// </summary>
    public static CliTraceEntry? Read(string message, IReadOnlyList<string> details, DateTimeOffset finishedAt)
    {
        var program = CliTraceDescription.Programs.FirstOrDefault(name => message.StartsWith(name + " ", StringComparison.Ordinal));
        if (program is null || details.Count == 0)
        {
            return null;
        }

        var header = HeaderLine().Match(details[0]);
        if (!header.Success)
        {
            return null;
        }

        var duration = TimeSpan.FromMilliseconds(long.Parse(header.Groups["ms"].ValueSpan, CultureInfo.InvariantCulture));
        int? exitCode = header.Groups["exit"].Value == "none" ? null : int.Parse(header.Groups["exit"].ValueSpan, CultureInfo.InvariantCulture);
        var (stdout, stderr) = Sections(details.Skip(1));
        var args = message[(program.Length + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var (_, title, session) = CliTraceDescription.Describe(args);
        var kind = CliTraceDescription.KindOf(program, args);
        var named = header.Groups["title"];
        return new CliTraceEntry(header.Groups["id"].Value, finishedAt - duration, duration, args, exitCode,
            header.Groups["status"].Value, stdout, stderr, kind, named.Success ? named.Value : title, session, program);
    }

    private static void Section(StringBuilder text, string marker, string content)
    {
        var trimmed = content.Trim();
        if (trimmed.Length > 0)
        {
            text.Append('\n').Append(marker).Append('\n').Append(trimmed);
        }
    }

    /// <summary>The lines under each marker, joined again; a marker line inside an output is read as the marker, which no <c>wslc</c> output has.</summary>
    private static (string Stdout, string Stderr) Sections(IEnumerable<string> lines)
    {
        var stdout = new List<string>();
        var stderr = new List<string>();
        List<string>? current = null;
        foreach (var line in lines)
        {
            switch (line)
            {
                case OutputMarker:
                    current = stdout;
                    break;
                case ErrorsMarker:
                    current = stderr;
                    break;
                default:
                    current?.Add(line);
                    break;
            }
        }

        return (string.Join('\n', stdout), string.Join('\n', stderr));
    }

    [GeneratedRegex(@"^Trace: (?<id>[0-9a-f]{32}) \| Status: (?<status>[a-z_]+) \| Exit code: (?<exit>-?\d+|none) \| Duration: (?<ms>\d+) ms(?: \| Title: (?<title>.+))?$")]
    private static partial Regex HeaderLine();
}
