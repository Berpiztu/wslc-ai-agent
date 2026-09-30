namespace WslcAgent.Server.Wslc;

/// <summary>How the agent runs the <c>wslc</c> CLI. Bound from the <c>Wslc</c> configuration section.</summary>
public sealed class WslcOptions
{
    public const string Section = "Wslc";

    /// <summary>Full path of <c>wslc.exe</c>. Empty: resolve <c>wslc</c> on the PATH.</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>
    /// WSLC session the agent operates on at start-up. Empty: the CLI's own
    /// store for this user (<see cref="SessionStores.Default"/>). Passed as
    /// <c>--session NAME</c> to every command that accepts it, except that one,
    /// which is named by naming none; changed at run time through
    /// <c>POST /api/v1/sessions/select</c>.
    /// </summary>
    public string? SelectedSession { get; set; }

    /// <summary>Timeout for a command that does not stream (seconds).</summary>
    public int DefaultTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// How long a whole-list read (<c>container list</c>, <c>container stats</c>)
    /// is served from memory (<see cref="CachingWslcRunner"/>), in seconds; 0
    /// turns the cache off. One window per client refresh (5 s, the list pages'
    /// and Home's) means one launch per interval however many clients poll.
    /// </summary>
    public double ReadCacheSeconds { get; set; } = 5;

    /// <summary>Where the agent keeps its own state (restart policies). Default: the per-user install folder's data directory.</summary>
    public string DataDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WSLC-AI-Agent", "data");

    /// <summary>Where the agent writes its own log, which the Logs page reads. Empty: <c>logs</c> under <see cref="DataDirectory"/>.</summary>
    public string? LogDirectory { get; set; }

    public string EffectiveLogDirectory =>
        string.IsNullOrWhiteSpace(LogDirectory) ? Path.Combine(DataDirectory, "logs") : LogDirectory;

    /// <summary>
    /// The package folder the agent was installed with: the installers it hands
    /// out to the clients (<c>wslc-ai-client.msi</c>, <c>wslc-ai-client.apk</c>)
    /// and its own (<c>wslc-ai-agent.msi</c>) for updating itself. The installer
    /// writes it to <c>wslc-ai-agent.ini</c>; Settings → Update overrides it.
    /// Empty: <c>C:\Berpiztu\wslc-ai-agent</c>. Created at start when missing.
    /// </summary>
    public string? ClientPackagesPath { get; set; }
}
