using System.Diagnostics;
using System.Text.Json;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Updates;

/// <summary>
/// The agent's releases on GitHub, for Settings → Update: the latest one, and
/// installing it with the README's own line (<see cref="LatestRelease.InstallLine"/>),
/// run on this machine as a person would paste it into a terminal.
/// </summary>
public sealed class GitHubReleases(HttpClient http, ILogger<GitHubReleases> logger)
{
    private const string LatestUrl = "https://api.github.com/repos/Berpiztu/wslc-ai-agent/releases/latest";

    /// <summary>GitHub allows 60 unauthenticated calls an hour; a release is not published by the minute.</summary>
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(10);

    private (LatestRelease Release, DateTimeOffset ReadAt)? _latest;

    /// <summary>The latest release, read again when the one known is older than ten minutes.</summary>
    public async Task<LatestRelease> LatestAsync(CancellationToken cancellationToken = default)
    {
        if (_latest is { } known && DateTimeOffset.UtcNow - known.ReadAt < Fresh)
        {
            return known.Release;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
        request.Headers.UserAgent.ParseAdd("wslc-ai-agent");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = json.RootElement;
        var release = new LatestRelease(
            (root.GetProperty("tag_name").GetString() ?? "").TrimStart('v'),
            root.TryGetProperty("published_at", out var published) && published.ValueKind == JsonValueKind.String ? published.GetDateTimeOffset() : null,
            root.TryGetProperty("html_url", out var url) ? url.GetString() ?? "" : "");
        _latest = (release, DateTimeOffset.UtcNow);
        return release;
    }

    /// <summary>
    /// Runs <see cref="LatestRelease.InstallLine"/> in a PowerShell window of its own
    /// on this machine, and returns at once. With the agent installed the line asks
    /// nothing: it puts the latest installers in the package folder and asks the
    /// agent to update itself, which the agent then does as Update now does.
    /// </summary>
    public void RunInstallLine()
    {
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell, $"-NoProfile -ExecutionPolicy Bypass -Command \"{LatestRelease.InstallLine}\"")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell did not start.");
        logger.LogWarning(AgentUpdater.LogPrefix + "Install from GitHub: {Line} runs in a PowerShell window on this machine", LatestRelease.InstallLine);
    }
}
