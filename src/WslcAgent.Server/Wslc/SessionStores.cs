namespace WslcAgent.Server.Wslc;

/// <summary>
/// Where WSLC keeps its sessions on this machine, and which of them the agent
/// can work in. One folder per session under <c>%LOCALAPPDATA%\wslc\sessions</c>;
/// two of them are the CLI's own. <c>wslc-cli-&lt;user&gt;</c> is what a command
/// that names no session opens — a session like any other, with a name like any
/// other, reached by naming none. <c>wslc-cli-admin-&lt;user&gt;</c> belongs to an
/// elevated <c>wslc</c> process: the agent does not run elevated and has no way
/// to, so it cannot open it, enter it or run anything in it, and does not offer
/// it. It is still one of the stores on disk, so
/// the System page, which is about what they occupy, keeps showing it.
/// </summary>
public static class SessionStores
{
    /// <summary>The CLI's own stores, which <c>system session enter</c> refuses.</summary>
    public const string ReservedPrefix = "wslc-cli-";

    /// <summary>The store of an elevated host process, which the agent cannot use at all.</summary>
    public const string AdminPrefix = "wslc-cli-admin-";

    private static string? _default;

    /// <summary>The sessions folder, or empty where the machine has no <c>LOCALAPPDATA</c>.</summary>
    public static string BasePath() => BasePath(Environment.GetEnvironmentVariable("LOCALAPPDATA"));

    /// <inheritdoc cref="BasePath()"/>
    public static string BasePath(string? localAppData) =>
        string.IsNullOrEmpty(localAppData) ? "" : Path.Combine(localAppData, "wslc", "sessions");

    /// <summary>One of the CLI's own stores, which cannot be entered by path.</summary>
    public static bool IsReserved(string name) => name.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>An elevated process's store: not offered, not started, not targeted.</summary>
    public static bool IsAdmin(string name) => name.StartsWith(AdminPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The store this user's CLI opens when a command names no session: the
    /// reserved store on disk that is not the elevated one. Until there is one
    /// to read, the name the CLI gives it answers, and disk is read again next
    /// time; once found it is kept, since it does not change under a running
    /// agent.
    /// </summary>
    public static string Default
    {
        get
        {
            if (_default is { } known)
            {
                return known;
            }

            var found = FindOnDisk();
            if (found is not null)
            {
                _default = found;
            }

            return found ?? $"{ReservedPrefix}{Environment.UserName}";
        }
    }

    /// <summary>
    /// The agent's own session: the one that travels as no <c>--session</c> at
    /// all, because a command that names none is what opens it — and, once it
    /// has been stopped, the only thing that opens it again.
    /// </summary>
    public static bool IsDefault(string name) => string.Equals(name, Default, StringComparison.OrdinalIgnoreCase);

    private static string? FindOnDisk()
    {
        var basePath = BasePath();
        if (basePath.Length == 0 || !Directory.Exists(basePath))
        {
            return null;
        }

        try
        {
            return new DirectoryInfo(basePath).EnumerateDirectories()
                .Select(dir => dir.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(name => IsReserved(name) && !IsAdmin(name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
