using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Overview;

/// <summary>
/// What the System page reads out of the CLI's text:
/// <c>wslc info</c>, the session
/// list, image dates, a prune's reclaimed space, and which VHDX compaction is
/// about.
/// </summary>
public static partial class SystemParsing
{
    private static readonly Dictionary<string, long> SizeUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["B"] = 1, ["KB"] = 1000, ["MB"] = 1000L * 1000, ["GB"] = 1000L * 1000 * 1000,
        ["TB"] = 1000L * 1000 * 1000 * 1000, ["PB"] = 1000L * 1000 * 1000 * 1000 * 1000,
        ["KIB"] = 1024, ["MIB"] = 1024L * 1024, ["GIB"] = 1024L * 1024 * 1024,
        ["TIB"] = 1024L * 1024 * 1024 * 1024, ["PIB"] = 1024L * 1024 * 1024 * 1024 * 1024,
    };

    /// <summary>The flattened <c>Client</c> / <c>Server</c> fields of <c>wslc info --format json</c>.</summary>
    public static SystemRuntimeInfo RuntimeInfo(JsonElement info)
    {
        if (info.ValueKind != JsonValueKind.Object)
        {
            return SystemRuntimeInfo.Empty;
        }

        var client = info.TryGetProperty("Client", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
        var server = info.TryGetProperty("Server", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
        return new SystemRuntimeInfo(
            ClientVersion: Field(client, "Version"),
            Kernel: Field(client, "KernelVersion"),
            Windows: Field(client, "WindowsVersion"),
            SessionManager: Field(server, "SessionManagerVersion"),
            Direct3D: Field(client, "Direct3DVersion"),
            DxCore: Field(client, "DxCoreVersion"),
            SettingsFile: Field(client, "SettingsFile"));
    }

    /// <summary>
    /// <c>wslc system session list</c>: a header, then columns separated by two
    /// or more spaces — id, creator PID, display name (which may hold spaces).
    /// </summary>
    public static IReadOnlyList<ActiveSession> ActiveSessions(string stdout)
    {
        var lines = stdout.Split('\n').Select(line => line.TrimEnd()).Where(line => line.Trim().Length > 0).ToList();
        return lines.Skip(1)
            .Select(line => ColumnGap().Split(line.Trim(), 3))
            .Where(parts => parts.Length > 0 && parts[0].Length > 0)
            .Select(parts => new ActiveSession(
                parts[0],
                parts.Length > 1 && parts[1].All(char.IsAsciiDigit) && int.TryParse(parts[1], out var pid) ? pid : null,
                parts.Length > 2 ? parts[2] : ""))
            .ToList();
    }

    /// <summary>
    /// An image's creation time: unix seconds, ISO, or the CLI's
    /// <c>2026-08-27 09:07:36 -0500 GMT-5</c>. Null when none of those.
    /// </summary>
    public static DateTimeOffset? Created(string text)
    {
        var cleaned = GmtTail().Replace(text.Trim(), "");
        if (cleaned.Length == 0)
        {
            return null;
        }

        if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var unix))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)(unix * 1000));
        }

        cleaned = CompactOffset().Replace(cleaned, "$1$2:$3");
        return DateTimeOffset.TryParse(cleaned, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>The CLI's own <c>Total reclaimed space: 1.2GB</c>, in bytes; null when it printed none.</summary>
    public static long? ReclaimedBytes(string text)
    {
        var match = Reclaimed().Match(text);
        if (!match.Success
            || !SizeUnits.TryGetValue(match.Groups["unit"].Value, out var multiplier)
            || !double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return (long)(value * multiplier);
    }

    /// <summary>The sentence a cleanup ends with: what WSLC reclaimed, and whether the files on disk shrank.</summary>
    public static string CleanupSummary(string target, long? reclaimedBytes, long storeDeltaBytes, long? imageDeltaBytes)
    {
        var label = target switch
        {
            "images" => "Image prune",
            "volumes" => "Volume prune",
            "networks" => "Network prune",
            _ => "Cleanup",
        };

        var parts = new List<string> { $"{label} completed." };
        if (reclaimedBytes is { } reclaimed)
        {
            parts.Add($"WSLC reported {Bytes.Humanize(reclaimed)} reclaimed inside the store.");
        }
        else if (imageDeltaBytes is > 0)
        {
            parts.Add($"Registered image size dropped by {Bytes.Humanize(imageDeltaBytes.Value)}.");
        }

        parts.Add(storeDeltaBytes > 0
            ? $"VHDX file length on disk shrank by {Bytes.Humanize(storeDeltaBytes)}."
            : "VHDX file length on disk did not shrink yet.");
        return string.Join(' ', parts);
    }

    /// <summary>
    /// Which session VHDX compaction talks about: the selected one, and no
    /// other. It is that session's <c>storage.vhdx</c> that gets compacted, and
    /// only that session holds that file open — another session running holds
    /// its own, in its own folder, and has nothing to do with this one.
    /// The name comes back only while it runs, which
    /// is what blocks compaction; the path is the selected store's either way.
    /// </summary>
    public static (string Session, string StoragePath) CompactionTarget(
        SessionStoreUsage store, IReadOnlyList<ActiveSession> active, string selected)
    {
        if (selected.Length == 0)
        {
            return ("", "");
        }

        var running = active.FirstOrDefault(s => s.DisplayName == selected);
        var storage = store.Sessions.FirstOrDefault(s => s.Name == selected)?.StoragePath ?? "";
        return (running?.DisplayName ?? "", storage);
    }

    private static string Field(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object ? parent.GetString(name).Trim() : "";

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex ColumnGap();

    [GeneratedRegex(@"\s+GMT[+-]?\d+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex GmtTail();

    [GeneratedRegex(@"\s*([+-])(\d{2})(\d{2})\s*$")]
    private static partial Regex CompactOffset();

    [GeneratedRegex(@"total\s+reclaimed\s+space\s*:\s*(?<value>[0-9]+(?:\.[0-9]+)?)\s*(?<unit>[kmgtp]?i?b|b)", RegexOptions.IgnoreCase)]
    private static partial Regex Reclaimed();
}
