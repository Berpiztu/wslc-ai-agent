namespace WslcAgent.UI.Links;

/// <summary>
/// Opening a link somewhere other than in the application: a published port's
/// public address, which is a page of its own and not part of the dashboard.
/// <para>
/// The browser does this itself (<c>window.open</c>) and reports
/// <see cref="Supported"/> false. A native client must not: its web view has no
/// second window, and asking for one takes MAUI's own chrome client through
/// <c>Uri.parse(null)</c> and kills the application — which is exactly what
/// tapping Remote did on Android. There the link goes to the system browser.
/// </para>
/// </summary>
public interface IClientLinks
{
    /// <summary>False in the browser: opening a tab belongs to the browser.</summary>
    bool Supported { get; }

    /// <summary>Hands the address to whatever opens pages on this device.</summary>
    Task OpenAsync(string url, CancellationToken cancellationToken = default);
}
