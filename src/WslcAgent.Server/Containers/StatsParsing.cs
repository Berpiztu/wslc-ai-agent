using System.Globalization;
using WslcAgent.ApiClient;

namespace WslcAgent.Server.Containers;

/// <summary>Reads the human strings <c>wslc stats</c> prints back into numbers, for the page aggregate.</summary>
public static class StatsParsing
{
    private static readonly (string Suffix, double Factor)[] Units =
    [
        ("KIB", 1024d), ("MIB", 1024d * 1024), ("GIB", 1024d * 1024 * 1024), ("TIB", 1024d * 1024 * 1024 * 1024),
        ("KB", 1e3), ("MB", 1e6), ("GB", 1e9), ("TB", 1e12),
        ("B", 1d),
    ];

    /// <summary><c>"0.40%"</c> → 0.4; anything unreadable → 0.</summary>
    public static double Percent(string text) => Percents.Parse(text);

    /// <summary><c>"885.1MiB / 7.6GiB"</c> → bytes of the used part; anything unreadable → 0.</summary>
    public static long UsedBytes(string memUsage)
    {
        var used = memUsage.Split('/', 2)[0].Trim();
        return Bytes(used);
    }

    /// <summary><c>"885.1MiB / 7.6GiB"</c>, <c>"1.2MB / 0B"</c> → the two byte counts.</summary>
    public static (long First, long Second) Pair(string text)
    {
        var parts = text.Split('/', 2);
        return (Bytes(parts[0]), parts.Length > 1 ? Bytes(parts[1]) : 0);
    }

    /// <summary><c>"885.1MiB"</c> → bytes (binary and decimal suffixes); anything unreadable → 0.</summary>
    public static long Bytes(string text)
    {
        var value = text.Trim().ToUpperInvariant();
        foreach (var (suffix, factor) in Units)
        {
            if (value.EndsWith(suffix, StringComparison.Ordinal))
            {
                var number = value[..^suffix.Length].Trim();
                return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? (long)Math.Round(parsed * factor)
                    : 0;
            }
        }

        return 0;
    }
}
