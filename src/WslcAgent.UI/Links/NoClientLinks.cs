namespace WslcAgent.UI.Links;

/// <summary>The browser host: it opens its own tabs, so nothing here is used.</summary>
public sealed class NoClientLinks : IClientLinks
{
    public bool Supported => false;

    public Task OpenAsync(string url, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The browser build opens links itself.");
}
