using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Updates;

/// <summary>The browser host: not a native client, so there is never an update to offer.</summary>
public sealed class NoClientUpdates : IClientUpdates
{
    public bool Supported => false;

    public string Platform => "";

    public string InstalledVersion => "";

    public int InstalledBuild => 0;

    public string DismissedVersion { get; set; } = "";

    public int DismissedBuild { get; set; }

    public Task InstallAsync(ClientPackageInfo package, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The browser build cannot install a native client.");
}
