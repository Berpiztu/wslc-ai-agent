using System.Globalization;
using System.Text.RegularExpressions;

namespace WslcAgent.Server.Images;

/// <summary>
/// What a <c>wslc image pull</c> terminal output says:
/// the layers seen and how far each got (downloading is the first half,
/// extracting the second), a status line to show, the error worth repeating,
/// and the log as text without terminal codes. A push draws the same layer
/// lines and is failed and logged alike; <see cref="PushProgress"/> reads its
/// steps with the helpers left internal here.
/// </summary>
public static partial class PullProgress
{
    public const int MaxLogChars = 200_000;

    private static readonly Dictionary<string, double> SizeFactors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["B"] = 1, ["KB"] = 1024, ["MB"] = 1024d * 1024, ["GB"] = 1024d * 1024 * 1024, ["TB"] = 1024d * 1024 * 1024 * 1024,
    };

    /// <summary>The percentage over every layer seen and the status line.</summary>
    public static (int Pct, string Status) Read(string output)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var progress = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var status = "Pulling...";

        foreach (var cleaned in Lines(output))
        {
            var match = LayerLine().Match(cleaned);
            var layer = match.Success ? match.Groups[1].Value.ToLowerInvariant() : "";
            var message = match.Success ? match.Groups[2].Value.Trim() : cleaned;
            if (layer.Length > 0)
            {
                seen.Add(layer);
            }

            if (message.Contains("Already exists") || message.Contains("Pull complete"))
            {
                Set(progress, layer, 1.0);
                status = message;
            }
            else if (message.Contains("Extracting"))
            {
                if (layer.Length > 0 && Sizes(message) is { } size)
                {
                    Raise(progress, layer, 0.5 + (0.5 * Math.Min(size.Current / size.Total, 1.0)));
                }

                status = "Extracting...";
            }
            else if (message.Contains("Download complete") || message.Contains("Verifying Checksum"))
            {
                Raise(progress, layer, 0.5);
                status = message;
            }
            else if (message.Contains("Downloading"))
            {
                if (layer.Length > 0 && Sizes(message) is { } size)
                {
                    Raise(progress, layer, 0.5 * Math.Min(size.Current / size.Total, 1.0));
                }

                // The word alone, as Extracting: the CLI's bar and sizes said in text
                // what the percentage beside it already shows.
                status = "Downloading...";
            }
            else if (message.Contains("Pulling from"))
            {
                status = "Pulling from registry...";
            }
            else if (message.Contains("Digest:"))
            {
                status = "Verifying digest...";
            }
            else if (message.Contains("Status:"))
            {
                foreach (var existing in seen)
                {
                    progress[existing] = 1.0;
                }

                status = "Downloaded";
            }
        }

        var pct = seen.Count == 0 ? 0 : Math.Clamp((int)(seen.Sum(l => progress.GetValueOrDefault(l)) / seen.Count * 100), 0, 100);
        if (seen.Count == 0 && output.Trim().Length > 0)
        {
            status = "Running (no incremental progress from wslc)";
        }

        return (pct, status);
    }

    /// <summary>
    /// What the CLI said when it failed, whole: its output without the layers'
    /// progress lines, as printed. The reason, the code and the
    /// closing line are one message, shown entire in the toast and in the log,
    /// never a line picked out of it — the picking is what showed "If this error
    /// was unexpected…" for a network that was unreachable. Null when nothing but
    /// progress was printed.
    /// </summary>
    public static string? Failure(string output)
    {
        var said = Lines(output).Where(line => !LayerLine().IsMatch(line)).ToList();
        return said.Count > 0 ? string.Join('\n', said) : null;
    }

    /// <summary>The terminal output as lines to read, one per update.</summary>
    public static (string Log, bool Truncated) Log(string raw)
    {
        var truncated = raw.Length > MaxLogChars;
        return (string.Join('\n', Lines(truncated ? raw[^MaxLogChars..] : raw)), truncated);
    }

    /// <summary>
    /// A pseudo console redraws every layer's progress in place by moving the cursor,
    /// not with line breaks: a cursor move or an erase is where one update ends, as a
    /// carriage return is. Other codes (colours) are only removed; blank lines dropped.
    /// </summary>
    internal static IEnumerable<string> Lines(string output) =>
        Escape().Replace(CursorMove().Replace(output, "\n"), "")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);

    internal static (double Current, double Total)? Sizes(string message)
    {
        var match = SizeProgress().Match(message);
        if (!match.Success)
        {
            return null;
        }

        var current = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * SizeFactors[match.Groups[2].Value];
        var total = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) * SizeFactors[match.Groups[4].Value];
        return total > 0 ? (current, total) : null;
    }

    internal static void Set(Dictionary<string, double> progress, string layer, double value)
    {
        if (layer.Length > 0)
        {
            progress[layer] = value;
        }
    }

    internal static void Raise(Dictionary<string, double> progress, string layer, double value)
    {
        if (layer.Length > 0)
        {
            progress[layer] = Math.Max(progress.GetValueOrDefault(layer), value);
        }
    }

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ABCDEFGHJKf]")]
    private static partial Regex CursorMove();

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]|\x1B\][^\x07]*\x07")]
    private static partial Regex Escape();

    [GeneratedRegex(@"([0-9a-f]{12}):\s*(.*)", RegexOptions.IgnoreCase)]
    internal static partial Regex LayerLine();

    [GeneratedRegex(@"([0-9]+(?:\.[0-9]+)?)\s*(B|KB|MB|GB|TB)\s*/\s*([0-9]+(?:\.[0-9]+)?)\s*(B|KB|MB|GB|TB)", RegexOptions.IgnoreCase)]
    private static partial Regex SizeProgress();
}
