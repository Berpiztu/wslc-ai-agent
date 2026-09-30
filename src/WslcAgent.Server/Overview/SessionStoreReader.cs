using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Overview;

/// <summary>
/// The session VHDX files under <c>%LOCALAPPDATA%\wslc\sessions</c>: one folder
/// per session holding <c>storage.vhdx</c> and <c>swap.vhdx</c>. File lengths,
/// not what WSLC holds inside them. Every store is read, the elevated process's
/// one included: the session picker leaves that one out because the agent
/// cannot work in it, but it takes up the same gigabytes as the rest and this
/// is what reports them. The agent runs on the Windows host, so it reads
/// them locally.
/// </summary>
public static class SessionStoreReader
{
    public static SessionStoreUsage Read() =>
        Read(Environment.GetEnvironmentVariable("LOCALAPPDATA"));

    internal static SessionStoreUsage Read(string? localAppData)
    {
        if (string.IsNullOrEmpty(localAppData))
        {
            return Empty("", "LOCALAPPDATA is not available on host.");
        }

        var basePath = SessionStores.BasePath(localAppData);
        if (!Directory.Exists(basePath))
        {
            return Empty(basePath, "WSLC session store was not found.");
        }

        try
        {
            var sessions = new DirectoryInfo(basePath).EnumerateDirectories()
                .OrderBy(dir => dir.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ReadSession)
                .OrderByDescending(session => session.TotalBytes)
                .ToList();
            var largest = sessions.FirstOrDefault();
            return new SessionStoreUsage(
                basePath,
                sessions,
                sessions.Sum(s => s.StorageBytes),
                sessions.Sum(s => s.SwapBytes),
                sessions.Sum(s => s.TotalBytes),
                largest?.Name ?? "",
                largest?.TotalBytes ?? 0,
                "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Empty(basePath, $"Could not read the WSLC session store: {ex.Message}");
        }
    }

    /// <summary>The same rows marked with which session runs and which one the agent targets.</summary>
    public static SessionStoreUsage Mark(SessionStoreUsage usage, IReadOnlyList<ActiveSession> active, string selected)
    {
        var running = active.Select(s => s.DisplayName).ToHashSet(StringComparer.Ordinal);
        return usage with
        {
            Sessions = usage.Sessions
                .Select(s => s with { IsActive = running.Contains(s.Name), IsSelected = selected.Length > 0 && s.Name == selected })
                .ToList(),
        };
    }

    private static SessionStore ReadSession(DirectoryInfo dir)
    {
        var storage = new FileInfo(Path.Combine(dir.FullName, "storage.vhdx"));
        var swap = new FileInfo(Path.Combine(dir.FullName, "swap.vhdx"));
        var storageBytes = storage.Exists ? storage.Length : 0;
        var swapBytes = swap.Exists ? swap.Length : 0;
        DateTimeOffset? modified = storage.Exists ? new DateTimeOffset(storage.LastWriteTimeUtc) : swap.Exists ? new DateTimeOffset(swap.LastWriteTimeUtc) : null;
        return new SessionStore(dir.Name, dir.FullName, storage.FullName, swap.FullName, storageBytes, swapBytes,
            storageBytes + swapBytes, modified, IsActive: false, IsSelected: false);
    }

    private static SessionStoreUsage Empty(string basePath, string error) => new(basePath, [], 0, 0, 0, "", 0, error);
}
