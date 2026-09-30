using System.Text.Json;
using System.Text.Json.Nodes;

namespace WslcAgent.Server.Browse;

/// <summary>One screencast picture: JPEG bytes at the browser's own pixel size, and the page it shows.</summary>
public sealed record BrowseFrame(byte[] Jpeg, int Width, int Height, string Url);

/// <summary>
/// The browser a session drives: a page on the host, its screencast and its
/// input. <see cref="HostBrowserSession"/> owns who watches; this owns what
/// the page does. Playwright in the agent, a fake in the tests.
/// </summary>
public interface IBrowserPage : IAsyncDisposable
{
    /// <summary>The page's current address.</summary>
    string Url { get; }

    /// <summary>False once the browser or its page has gone.</summary>
    bool IsAlive { get; }

    /// <summary>The page zoom, 50 to 150.</summary>
    int ZoomPercent { get; }

    /// <summary>A screencast picture arrived (already acknowledged).</summary>
    event Action<BrowseFrame>? Frame;

    /// <summary>The page navigated; the argument is its new address.</summary>
    event Action<string>? Navigated;

    /// <summary>Launches the browser and loads <paramref name="url"/> at that viewport.</summary>
    Task OpenAsync(string url, int width, int height, CancellationToken cancellationToken);

    /// <summary>Viewport and screencast both become that size of CSS pixels.</summary>
    Task SetViewportAsync(int width, int height);

    Task StartScreencastAsync(int width, int height);

    Task StopScreencastAsync();

    /// <summary>After a resize (the soft keyboard came up): scrolls a focused field back into view if it left it.</summary>
    Task RevealIfCoveredAsync();

    /// <summary>
    /// Drives the page from one pane message (pointer, key, navigate, back,
    /// forward, reload, zoom, selection, copy); a returned object is the
    /// reply to that pane.
    /// </summary>
    Task<JsonObject?> HandleAsync(string type, JsonElement message);
}

/// <summary>Makes the browser pages sessions drive.</summary>
public interface IBrowserPageFactory
{
    IBrowserPage Create();
}
