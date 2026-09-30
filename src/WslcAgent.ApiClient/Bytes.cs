using System.Globalization;

namespace WslcAgent.ApiClient;

/// <summary>Byte counts as people read them; shared by every host that shows sizes.</summary>
public static class Bytes
{
    private static readonly string[] BinaryUnits = ["B", "KiB", "MiB", "GiB", "TiB"];

    /// <summary>Bytes → the shortest form with binary units, e.g. <c>1.2 GiB</c>.</summary>
    public static string Humanize(long bytes)
    {
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < BinaryUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value.ToString("0.#", CultureInfo.InvariantCulture)} {BinaryUnits[unit]}";
    }
}
