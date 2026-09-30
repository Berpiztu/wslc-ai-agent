using System.Globalization;

namespace WslcAgent.UI.Components;

/// <summary>Compact bytes for chart titles (<c>652.5MB</c>, <c>3.19GB</c>), the same the chart axis writes.</summary>
public static class ChartBytes
{
    private static readonly (string Unit, double Size)[] Units =
    [
        ("TB", Math.Pow(1024, 4)), ("GB", Math.Pow(1024, 3)), ("MB", Math.Pow(1024, 2)), ("KB", 1024),
    ];

    /// <summary>Two decimals under ten of a unit, one above; whole bytes below a kilobyte.</summary>
    public static string Compact(double bytes)
    {
        var abs = Math.Abs(bytes);
        foreach (var (unit, size) in Units)
        {
            if (abs >= size)
            {
                var format = abs < 10 * size || unit == "TB" ? "0.00" : "0.0";
                return (bytes / size).ToString(format, CultureInfo.InvariantCulture) + unit;
            }
        }

        return Math.Round(bytes).ToString(CultureInfo.InvariantCulture) + "B";
    }
}
