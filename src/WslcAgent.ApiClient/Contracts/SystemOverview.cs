namespace WslcAgent.ApiClient.Contracts;

/// <summary>Body of <c>GET /api/v1/system</c>: what the System page shows about the WSLC host.</summary>
/// <param name="Version">What <c>wslc version</c> printed, or why it could not run.</param>
/// <param name="Info">The runtime fields of <c>wslc info</c>; empty strings on a CLI without the command.</param>
/// <param name="InfoError">Why <c>wslc info</c> failed; empty when it did not.</param>
/// <param name="Store">The session VHDX files on disk.</param>
/// <param name="ActiveSessions">Running sessions, as <c>wslc system session list</c> prints them.</param>
/// <param name="SessionsError">Why the running sessions could not be listed; empty when they could.</param>
/// <param name="SelectedSession">The session the agent targets, always by name: the CLI's own store for this user when none was chosen.</param>
/// <param name="PrimaryActiveSession">The session compaction talks about — the selected one — named only while it runs; empty when it does not, whatever else is running.</param>
/// <param name="ActiveStoragePath">The selected session's <c>storage.vhdx</c>: the file Compact VHDX works on, running or not.</param>
/// <param name="CompactionBlocked">True while the selected session runs and holds that file open: Compact VHDX never terminates one. Another session running blocks nothing — it holds its own.</param>
/// <param name="TopImages">The largest images, biggest first (eight at most).</param>
/// <param name="ImageCount">Every registered image.</param>
/// <param name="ImageTotalBytes">Their sizes added up.</param>
/// <param name="ImageError">Why the images could not be listed; empty when they could.</param>
public sealed record SystemOverview(
    string Version,
    SystemRuntimeInfo Info,
    string InfoError,
    SessionStoreUsage Store,
    IReadOnlyList<ActiveSession> ActiveSessions,
    string SessionsError,
    string SelectedSession,
    string PrimaryActiveSession,
    string ActiveStoragePath,
    bool CompactionBlocked,
    IReadOnlyList<SystemImage> TopImages,
    int ImageCount,
    long ImageTotalBytes,
    string ImageError);

/// <summary>The <c>Client</c> and <c>Server</c> fields of <c>wslc info</c> the page lists.</summary>
public sealed record SystemRuntimeInfo(
    string ClientVersion,
    string Kernel,
    string Windows,
    string SessionManager,
    string Direct3D,
    string DxCore,
    string SettingsFile)
{
    public static readonly SystemRuntimeInfo Empty = new("", "", "", "", "", "", "");

    public bool IsEmpty => this == Empty;
}

/// <summary>The session store under <c>%LOCALAPPDATA%\wslc\sessions</c>: file lengths, not live payload.</summary>
/// <param name="BasePath">The store root.</param>
/// <param name="Sessions">One row per session folder, biggest first.</param>
/// <param name="LargestSessionName">The biggest session folder; empty when there is none.</param>
/// <param name="Error">Why the store could not be read; empty when it could.</param>
public sealed record SessionStoreUsage(
    string BasePath,
    IReadOnlyList<SessionStore> Sessions,
    long TotalStorageBytes,
    long TotalSwapBytes,
    long TotalBytes,
    string LargestSessionName,
    long LargestSessionBytes,
    string Error);

/// <summary>One session folder of the store.</summary>
/// <param name="LastModified">When <c>storage.vhdx</c> (else <c>swap.vhdx</c>) was last written; null when neither exists.</param>
/// <param name="IsActive">The session runs now.</param>
/// <param name="IsSelected">The agent targets this session.</param>
public sealed record SessionStore(
    string Name,
    string Path,
    string StoragePath,
    string SwapPath,
    long StorageBytes,
    long SwapBytes,
    long TotalBytes,
    DateTimeOffset? LastModified,
    bool IsActive,
    bool IsSelected);

/// <summary>One line of <c>wslc system session list</c>.</summary>
public sealed record ActiveSession(string Id, int? CreatorPid, string DisplayName);

/// <summary>An image row of the System page.</summary>
/// <param name="Repository">Empty for a dangling image.</param>
/// <param name="Tag">Empty when the CLI printed none.</param>
/// <param name="Created">Null when the CLI's text could not be read.</param>
public sealed record SystemImage(string Repository, string Tag, long SizeBytes, DateTimeOffset? Created);

/// <summary>Body of <c>POST /api/v1/system/cleanup/{target}</c>.</summary>
/// <param name="Target"><c>images</c>, <c>volumes</c> or <c>networks</c>.</param>
/// <param name="Command">The command line that ran.</param>
/// <param name="Message">What the CLI printed, or <c>Completed: COMMAND</c>.</param>
/// <param name="Summary">The sentence the user reads: what was reclaimed and whether the VHDX shrank.</param>
/// <param name="ReclaimedBytes">The CLI's own "Total reclaimed space", when it printed one.</param>
/// <param name="StoreDeltaBytes">How much the session VHDX files shrank on disk.</param>
/// <param name="ImageDeltaBytes">How much the registered image sizes dropped (images only).</param>
public sealed record CleanupResult(
    string Target,
    string Command,
    string Message,
    string Summary,
    long? ReclaimedBytes,
    long StoreDeltaBytes,
    long? ImageDeltaBytes);
