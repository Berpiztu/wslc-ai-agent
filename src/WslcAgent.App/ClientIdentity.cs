using System.Diagnostics;
using System.Reflection;

namespace WslcAgent.App;

/// <summary>
/// Installed display version and build of this client, the same values the
/// agent reads from the installer it advertises. On unpackaged Windows,
/// <see cref="AppInfo"/> reports a leftover package identity, so the EXE's
/// version resource is read instead: ProductVersion is the display version
/// and FileVersion's last part the build.
/// </summary>
internal static class ClientIdentity
{
    public static string DisplayVersion { get; } = ReadDisplayVersion();

    public static int Build { get; } = ReadBuild();

    private static string ReadDisplayVersion()
    {
#if WINDOWS
        var info = ExecutableInfo();
        var version = WithoutBuildMetadata(info?.ProductVersion) ?? WithoutBuildMetadata(info?.FileVersion);
        if (version is not null)
        {
            return version;
        }
#endif
        return AppInfo.Current.VersionString ?? "";
    }

    private static int ReadBuild()
    {
#if WINDOWS
        var info = ExecutableInfo();
        if (info is not null)
        {
            return info.FilePrivatePart > 0 ? info.FilePrivatePart : info.FileBuildPart;
        }
#endif
        return int.TryParse(AppInfo.Current.BuildString, out var build) ? build : 0;
    }

    private static FileVersionInfo? ExecutableInfo()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            // Beside the application, by its own name: an assembly's Location is
            // empty when the client is published as a single file.
            path = Path.Combine(AppContext.BaseDirectory, (Assembly.GetEntryAssembly()?.GetName().Name ?? "wslc-ai-client") + ".exe");
        }

        return string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? null : FileVersionInfo.GetVersionInfo(path);
    }

    /// <summary>"0.1.6+abc123" reads 0.1.6.</summary>
    private static string? WithoutBuildMetadata(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var plus = value.IndexOf('+');
        return plus >= 0 ? value[..plus] : value.Trim();
    }
}
