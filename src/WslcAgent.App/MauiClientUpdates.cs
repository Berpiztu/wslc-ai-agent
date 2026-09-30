using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI;
using WslcAgent.UI.Updates;

namespace WslcAgent.App;

/// <summary>
/// The native client's side of the update check: its platform and installed
/// version, the "forget this version" preference, and the download-and-install
/// step (msiexec on Windows, the package installer on Android).
/// </summary>
internal sealed class MauiClientUpdates(IServiceProvider services) : IClientUpdates
{
    private const string DismissedVersionKey = "client.update.dismissed-version";
    private const string DismissedBuildKey = "client.update.dismissed-build";

    public bool Supported => DeviceInfo.Platform == DevicePlatform.WinUI || DeviceInfo.Platform == DevicePlatform.Android;

    public string Platform => DeviceInfo.Platform == DevicePlatform.Android ? "android" : "windows";

    public string InstalledVersion => ClientIdentity.DisplayVersion;

    public int InstalledBuild => ClientIdentity.Build;

    public string DismissedVersion
    {
        get => Preferences.Default.Get(DismissedVersionKey, "");
        set => Preferences.Default.Set(DismissedVersionKey, value);
    }

    public int DismissedBuild
    {
        get => Preferences.Default.Get(DismissedBuildKey, 0);
        set => Preferences.Default.Set(DismissedBuildKey, value);
    }

    public async Task InstallAsync(ClientPackageInfo package, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var destination = Path.Combine(FileSystem.CacheDirectory, package.Filename);
        // The same handler the rest of the app talks through, so the download carries
        // this client's session: an agent that asks for a login refuses a bare client,
        // and accepting the update answered "Sign in at /login" instead of installing.
        // No timeout: the APK is tens of megabytes and may travel over a slow link.
        using var http = new HttpClient(UiServiceCollectionExtensions.Http(services))
        {
            BaseAddress = new Uri(AgentAddress.Resolve()),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        try
        {
            await new WslcAgentApi(http).DownloadClientPackageAsync(Platform, destination, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Cancelled from the toast: what was written is half an installer.
            try
            {
                File.Delete(destination);
            }
            catch (IOException)
            {
                // It will be overwritten by the next attempt anyway.
            }

            throw;
        }

        Launch(destination);
    }

    private static void Launch(string path)
    {
#if WINDOWS
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "msiexec.exe",
            Arguments = $"/i \"{path}\"",
            UseShellExecute = true,
        });
        // The MSI cannot replace this EXE while it runs: Programs and Features
        // would show the new version and the process would stay old.
        Application.Current?.Quit();
#elif ANDROID
        if (!ApkInstaller.TryLaunch(path, out var reason))
        {
            throw new InvalidOperationException(reason);
        }
#else
        throw new InvalidOperationException("This platform has no installer.");
#endif
    }
}
