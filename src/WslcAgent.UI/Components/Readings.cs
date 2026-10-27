using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>How a reading is written: the muted dash every list and card shows when there is none, and the figures of a volume's or a network's dials.</summary>
public static class Readings
{
    public static string Dash(string value) => string.IsNullOrEmpty(value) ? "—" : value;

    /// <summary>
    /// How long ago something was made, short, so a card's line has room for more: <c>&lt;1m ago</c>, <c>25m ago</c>,
    /// <c>1h 25m ago</c>, <c>3d 4h ago</c>, then days alone (<c>45d ago</c>). From the time wslc prints
    /// (<c>2026-10-26 23:02:53 -0500 GMT-5</c>); <paramref name="otherwise"/>, wslc's own words (<c>13 minutes ago</c>), when it reads as none.
    /// </summary>
    public static string Age(string createdAt, string otherwise)
    {
        var match = System.Text.RegularExpressions.Regex.Match(createdAt, @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) ([+-])(\d{2})(\d{2})");
        if (!match.Success
            || !DateTime.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var local))
        {
            return Dash(otherwise);
        }

        var offset = new TimeSpan(int.Parse(match.Groups[3].Value), int.Parse(match.Groups[4].Value), 0);
        var age = DateTimeOffset.UtcNow - new DateTimeOffset(local, match.Groups[2].Value == "-" ? -offset : offset);
        var since = age.TotalMinutes < 1 ? "<1m"
            : age.TotalHours < 1 ? $"{(int)age.TotalMinutes}m"
            : age.TotalDays < 1 ? $"{(int)age.TotalHours}h {age.Minutes}m"
            : age.TotalDays < 7 ? $"{(int)age.TotalDays}d {age.Hours}h"
            : $"{(int)age.TotalDays}d";
        return $"{since} ago";
    }

    /// <summary>
    /// The used side of <c>899.5MiB / 30.98GiB</c>: the total is the machine's,
    /// the same for every container, so what changes is shown and the whole
    /// reading stays in a tooltip.
    /// </summary>
    public static string MemoryUsed(string usage) => usage.Split('/')[0].Trim();

    /// <summary>The containers' CPU as Home writes it: what they use of all there is, "12.00% / 800%".</summary>
    public static string Cpu(HomeRuntime runtime) =>
        $"{runtime.CpuUsedPercent.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}% / {runtime.CpuTotalPercent}%";

    /// <summary>The figure inside a traffic dial, compact; nothing for a resource no container uses, so the dial shows its dash.</summary>
    public static string Traffic(bool inUse, long bytes) => inUse ? ChartBytes.Compact(bytes) : "";

    /// <summary>A traffic dial's tooltip: what the resource's containers moved, of what every container did.</summary>
    /// <param name="where">How the containers relate to the resource: "that mount this volume", "on this network".</param>
    public static string TrafficTitle(long bytes, string verb, int containers, string where, long total) =>
        $"{ChartBytes.Compact(bytes)} {verb} by the {containers} container{(containers == 1 ? "" : "s")} {where}, of {ChartBytes.Compact(total)} by all";
}
