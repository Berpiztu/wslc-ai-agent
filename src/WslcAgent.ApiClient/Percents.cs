using System.Globalization;

namespace WslcAgent.ApiClient;

/// <summary>
/// Reads the percentages <c>wslc stats</c> prints — <c>"0.40%"</c> — back into
/// numbers. It lives beside <see cref="Bytes"/> because both the agent (for the
/// page aggregate) and the clients (for the dials on a card) read the same
/// strings, and one reading of them is enough.
/// </summary>
public static class Percents
{
    /// <summary>A percent as a reading is written, <c>"93.5%"</c>, whatever the device's culture: a dial reads it back with <see cref="Parse"/>.</summary>
    public static string Text(double value) => $"{value.ToString("0.#", CultureInfo.InvariantCulture)}%";

    /// <summary><c>"0.40%"</c> → 0.4, <c>"0,40%"</c> → 0.4; anything unreadable → 0.</summary>
    public static double Parse(string text)
    {
        var value = text.Trim().TrimEnd('%').Trim();
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariant))
        {
            return invariant;
        }

        // A client may have been handed a number written the way its own machine
        // writes one, with a comma for the point.
        return double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var local) ? local : 0;
    }
}
