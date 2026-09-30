using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Updates;

/// <summary>
/// Compares the running native client with the package the agent advertises,
/// on every navigation: the agent's installer is the
/// source of truth, the check is cheap and repeats with a short cooldown, and
/// the outcome paints the brand version (green current, yellow stale).
/// </summary>
public sealed class ClientUpdateChecker(WslcAgentApi api, IClientUpdates client)
{
    private static readonly TimeSpan RecheckCooldown = TimeSpan.FromSeconds(3);
    private DateTime _lastCheckUtc = DateTime.MinValue;
    private string? _offeredKey;

    /// <summary>The package the agent advertises, once it has answered.</summary>
    public ClientPackageInfo? Package { get; private set; }

    /// <summary>Null until the agent answers; true when this client is not behind the agent's package.</summary>
    public bool? IsCurrent { get; private set; }

    /// <summary>True while a download or an installer launch is in progress.</summary>
    public bool Installing { get; private set; }

    public event Action? Changed;

    /// <summary>
    /// Asks the agent and returns the package when it is newer than this
    /// client, not forgotten by the user and not offered before; null otherwise.
    /// <paramref name="force"/> is the user asking (a tap on the version): no
    /// cooldown, a forgotten version offered again, an offer repeated.
    /// </summary>
    public async Task<ClientPackageInfo?> CheckAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (!client.Supported)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        if (!force && now - _lastCheckUtc < RecheckCooldown)
        {
            return null;
        }

        if (force)
        {
            _offeredKey = null;
            client.DismissedVersion = "";
            client.DismissedBuild = 0;
        }

        _lastCheckUtc = now;
        ClientPackageInfo package;
        try
        {
            package = await api.GetClientPackageAsync(client.Platform, cancellationToken);
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException or OperationCanceledException)
        {
            _lastCheckUtc = DateTime.MinValue;
            return null;
        }

        Package = package;
        var newer = package.IsNewerThan(client.InstalledVersion, client.InstalledBuild);
        if (package.Version.Length > 0 || package.Build > 0)
        {
            IsCurrent = !newer;
            Changed?.Invoke();
        }

        if (!newer || IsForgotten(package))
        {
            return null;
        }

        var key = $"{package.Version}|{package.Build}";
        if (key == _offeredKey)
        {
            return null;
        }

        _offeredKey = key;
        return package;
    }

    /// <summary>"Forget this version": no more offers until a newer package appears.</summary>
    public void Forget(ClientPackageInfo package)
    {
        client.DismissedVersion = package.Version;
        client.DismissedBuild = package.Build;
    }

    /// <summary>Downloads and launches the installer, reporting the download; exceptions carry the message for the user.</summary>
    public async Task InstallAsync(ClientPackageInfo package, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (Installing)
        {
            return;
        }

        if (!package.Available)
        {
            throw new InvalidOperationException(package.Error.Length > 0 ? package.Error : "The installer is not on this agent.");
        }

        Installing = true;
        Changed?.Invoke();
        try
        {
            await client.InstallAsync(package, progress, cancellationToken);
        }
        finally
        {
            Installing = false;
            Changed?.Invoke();
        }
    }

    private bool IsForgotten(ClientPackageInfo package) =>
        (client.DismissedVersion.Length > 0 || client.DismissedBuild > 0)
        && !package.IsNewerThan(client.DismissedVersion, client.DismissedBuild);
}
