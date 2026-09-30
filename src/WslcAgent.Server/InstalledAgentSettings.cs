using System.Runtime.Versioning;
using Microsoft.Win32;

namespace WslcAgent.Server;

/// <summary>
/// Where an installed agent listens. The Windows installer writes the bind
/// host and port chosen at install time under HKCU; an explicit
/// <c>--urls</c> argument or <c>ASPNETCORE_URLS</c> always wins over them.
/// </summary>
public static class InstalledAgentSettings
{
    public const string RegistryKey = @"Software\Berpiztu\wslc-agent\Agent";
    public const string DefaultBindHost = "127.0.0.1";
    public const int DefaultPort = 8069;

    /// <summary>The URL to listen on when nothing else was configured.</summary>
    public static string ListenUrl()
    {
        var host = DefaultBindHost;
        var port = DefaultPort;
        if (OperatingSystem.IsWindows())
        {
            (host, port) = ReadRegistry(host, port);
        }

        return $"http://{host}:{port}";
    }

    /// <summary>
    /// The installed agent leaves its version under its key at every start, so
    /// an older installer can say which newer version it is about to replace;
    /// installers before 0.1.47 wrote none. Only the agent that runs from the
    /// installed folder writes it: a development build must not pass for it.
    /// </summary>
    public static void RecordVersion(string version)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
            if (key is not null && RunsFromInstallDir(key))
            {
                key.SetValue("Version", version);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // The key is not ours to write: the installer's record stays as it is.
        }
    }

    /// <summary>
    /// Whether this process is the agent the installer put in place: it runs
    /// from the folder the installer recorded. A development build is not, and
    /// must neither pass for it nor be replaced by its installer.
    /// </summary>
    public static bool IsInstalledAgent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
        return key is not null && RunsFromInstallDir(key);
    }

    [SupportedOSPlatform("windows")]
    private static bool RunsFromInstallDir(RegistryKey key) =>
        key.GetValue("InstallDir") is string installDir && SameFolder(installDir, AppContext.BaseDirectory);

    private static bool SameFolder(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);

    [SupportedOSPlatform("windows")]
    private static (string Host, int Port) ReadRegistry(string host, int port)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
        if (key is null)
        {
            return (host, port);
        }

        if (key.GetValue("BindHost") is string bindHost && !string.IsNullOrWhiteSpace(bindHost))
        {
            host = bindHost.Trim();
        }

        if (key.GetValue("Port") is string portText && int.TryParse(portText, out var parsed) && parsed is > 0 and < 65536)
        {
            port = parsed;
        }

        return (host, port);
    }
}
