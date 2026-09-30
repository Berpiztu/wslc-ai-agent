using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Updates;

/// <summary>
/// What the host knows about the native client it is: which installer to ask
/// the agent for, what is installed, and how to run an update. The browser
/// host is not a native client and reports <see cref="Supported"/> false.
/// </summary>
public interface IClientUpdates
{
    /// <summary>False in the browser: nothing to update there.</summary>
    bool Supported { get; }

    /// <summary><c>windows</c> or <c>android</c>, the agent's package platform.</summary>
    string Platform { get; }

    /// <summary>Display version of the running client (Windows EXE ProductVersion / Android versionName).</summary>
    string InstalledVersion { get; }

    /// <summary>Build of the running client (Windows FileVersion's last part / Android versionCode).</summary>
    int InstalledBuild { get; }

    /// <summary>The version the user chose to forget; offers stop until a newer one appears.</summary>
    string DismissedVersion { get; set; }

    int DismissedBuild { get; set; }

    /// <summary>
    /// Downloads the package from the agent and hands it to the platform
    /// installer; throws with a message for the user when it cannot. The
    /// download reports itself to <paramref name="progress"/>, which is what
    /// the toast shows while it waits — an installer is tens of megabytes and
    /// a silent wait reads as nothing happening.
    /// </summary>
    Task InstallAsync(ClientPackageInfo package, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default);
}
