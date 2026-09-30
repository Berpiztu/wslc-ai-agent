using System.Text.RegularExpressions;

namespace WslcAgent.ApiClient;

/// <summary>
/// Text typed into a multi-value field, read as the list of CLI values behind
/// it. The list is what the form keeps; this is only how a hand-typed line is
/// understood, and how a list is shown on one line.
/// </summary>
public static partial class ValueList
{
    /// <summary>Plain repeatable values (ports, mounts): one per line or per comma.</summary>
    public static List<string> Split(string text) =>
        [.. text.Split(['\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>
    /// <c>KEY=value</c> values (env, labels, extra options): one per line, and a
    /// line is split on commas only when every chunk starts a new <c>KEY=</c>,
    /// because a single-line field is where <c>A=1, B=2</c> gets typed.
    /// <c>OPTS=a,b</c> and <c>MSG=hello, world</c> stay one value: the comma
    /// there belongs to the value.
    /// </summary>
    public static List<string> SplitPairs(string text)
    {
        var values = new List<string>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var chunks = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (chunks.Length > 1 && chunks.All(chunk => PairKey().IsMatch(chunk)))
            {
                values.AddRange(chunks);
            }
            else
            {
                values.Add(line);
            }
        }

        return values;
    }

    /// <summary>The values on one line, as a field shows them.</summary>
    public static string Join(IEnumerable<string> values) => string.Join(", ", values);

    /// <summary><c>KEY=</c> opening a chunk: an env, label or option key.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_.\-]+=")]
    private static partial Regex PairKey();
}
