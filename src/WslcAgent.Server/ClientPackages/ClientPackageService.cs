using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.ClientPackages;

/// <summary>
/// Finds the client installers and reads the version each one installs. The
/// installer is the source of truth, not a loose exe or a marker file: it is
/// what the client will actually get, so the update check compares it against
/// the running app (see <see cref="ClientPackageInfo.IsNewerThan"/>).
/// </summary>
public sealed class ClientPackageService(PackageFolders folders) : IClientPackageService
{
    private static readonly IReadOnlyDictionary<string, PackageSpec> Specs = new Dictionary<string, PackageSpec>(StringComparer.OrdinalIgnoreCase)
    {
        ["windows"] = new("wslc-ai-client.msi", "application/x-msi", "The Windows client installer", "build-client-installer.ps1"),
        ["android"] = new("wslc-ai-client.apk", "application/vnd.android.package-archive", "The Android APK", "build-client-apk.ps1"),
    };

    public ClientPackageInfo Describe(string platform)
    {
        var spec = Spec(platform);
        var path = Locate(platform);
        if (path is null)
        {
            return new ClientPackageInfo(platform.ToLowerInvariant(), spec.Filename, false, "", 0,
                $"{spec.What} is not on this agent. Put {spec.Filename} in {folders.Folder}: it comes with every release, or .\\{spec.BuildScript} builds it.");
        }

        var (version, build) = spec.Filename.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)
            ? ApkManifestVersion.Read(path)
            : (MsiProductVersion.Read(path), 0);
        return new ClientPackageInfo(platform.ToLowerInvariant(), spec.Filename, true, version, build, "");
    }

    public string? Locate(string platform) => folders.Locate(Spec(platform).Filename);

    public string MediaType(string platform) => Spec(platform).MediaType;

    private static PackageSpec Spec(string platform) =>
        Specs.TryGetValue(platform, out var spec)
            ? spec
            : throw new ArgumentException("Platform must be 'windows' or 'android'.", nameof(platform));

    private sealed record PackageSpec(string Filename, string MediaType, string What, string BuildScript);
}
