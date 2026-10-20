using System.Diagnostics;
using System.Text.Json;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;

namespace WslcAgent.Server.Updates;

/// <summary>
/// The agent's releases on GitHub, for Settings → Update: the newest one for
/// the agent running, and installing it with the README's own line
/// (<see cref="LatestRelease.InstallCommand"/>), run on this machine as a
/// person would paste it into a terminal.
/// <para>
/// Which is the newest depends on what the agent is. One that came from an
/// official release, or a build of one, is offered official releases only, as
/// <c>releases/latest</c> would. One that came from a preview (a pre-release
/// published from develop), or a build of one, is offered the newest of all:
/// the next preview, or an official release above it. What it came from is
/// the release with the highest version not above its own.
/// </para>
/// </summary>
public sealed class GitHubReleases(HttpClient http, IAgentInfo agent, ILogger<GitHubReleases> logger)
{
    /// <summary>The most recent releases, newest first: far more than the previews published between two official ones.</summary>
    private const string ReleasesUrl = "https://api.github.com/repos/Berpiztu/wslc-ai-agent/releases?per_page=50";

    /// <summary>GitHub allows 60 unauthenticated calls an hour; a release is not published by the minute.</summary>
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(10);

    private (LatestRelease Release, DateTimeOffset ReadAt)? _latest;

    /// <summary>The newest release for this agent, read again when the one known is older than ten minutes.</summary>
    public async Task<LatestRelease> LatestAsync(CancellationToken cancellationToken = default)
    {
        if (_latest is { } known && DateTimeOffset.UtcNow - known.ReadAt < Fresh)
        {
            return known.Release;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
        request.Headers.UserAgent.ParseAdd("wslc-ai-agent");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var published = json.RootElement.EnumerateArray()
            .Where(release => !release.GetProperty("draft").GetBoolean())
            .Select(Read)
            .OfType<Published>()
            .ToList();

        var running = Comparable(agent.Version);
        var cameFrom = running is null ? null : published.Where(release => release.Number <= running).MaxBy(release => release.Number);
        var offered = cameFrom is { Release.Preview: true } ? published : published.Where(release => !release.Release.Preview);
        var newest = offered.MaxBy(release => release.Number)
            ?? throw new HttpRequestException("GitHub lists no release of the agent.");
        _latest = (newest.Release, DateTimeOffset.UtcNow);
        return newest.Release;
    }

    /// <summary>
    /// Runs the newest release's <see cref="LatestRelease.InstallCommand"/> in a
    /// PowerShell window of its own on this machine, and returns once it is
    /// started. With the agent installed the line asks nothing: it puts that
    /// release's installers in the package folder and asks the agent to update
    /// itself, which the agent then does as Update now does.
    /// </summary>
    public async Task RunInstallLineAsync(CancellationToken cancellationToken = default)
    {
        var line = (await LatestAsync(cancellationToken)).InstallCommand();
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell, $"-NoProfile -ExecutionPolicy Bypass -Command \"{line}\"")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell did not start.");
        logger.LogWarning(AgentUpdater.LogPrefix + "Install from GitHub: {Line} runs in a PowerShell window on this machine", line);
    }

    /// <summary>One release as GitHub lists it; null when its tag carries no version.</summary>
    private static Published? Read(JsonElement release)
    {
        var version = (release.GetProperty("tag_name").GetString() ?? "").TrimStart('v');
        if (Comparable(version) is not { } number)
        {
            return null;
        }

        return new Published(
            new LatestRelease(
                version,
                release.TryGetProperty("published_at", out var at) && at.ValueKind == JsonValueKind.String ? at.GetDateTimeOffset() : null,
                release.TryGetProperty("html_url", out var url) ? url.GetString() ?? "" : "",
                release.TryGetProperty("prerelease", out var preview) && preview.GetBoolean()),
            number);
    }

    private sealed record Published(LatestRelease Release, Version Number);

    /// <summary>
    /// A version with all four parts: a release is written <c>1.6.4</c> and
    /// its first build <c>1.6.4.1</c>, and <see cref="Version"/> would put
    /// <c>1.6.4</c> below <c>1.6.4.0</c>. Null when there is no number.
    /// </summary>
    private static Version? Comparable(string text) =>
        Version.TryParse(text.Split('-', '+', ' ')[0], out var version)
            ? new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0))
            : null;
}
