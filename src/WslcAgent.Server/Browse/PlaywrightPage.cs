using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace WslcAgent.Server.Browse;

/// <summary>
/// A headless Edge on the host (Chromium when Edge will not start), driven
/// through Playwright and its DevTools session: the page at the container's
/// loopback port, its screencast, and the pointer and keys a pane sends.
/// </summary>
public sealed class PlaywrightPage(ILogger<PlaywrightPage> logger) : IBrowserPage
{
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HistoryTimeout = TimeSpan.FromSeconds(15);

    /// <summary>What the pane sends for a key that Playwright names differently.</summary>
    private static readonly Dictionary<string, string> KeyAliases = new()
    {
        [" "] = "Space",
        ["Spacebar"] = "Space",
        ["Esc"] = "Escape",
        ["Del"] = "Delete",
        ["Left"] = "ArrowLeft",
        ["Right"] = "ArrowRight",
        ["Up"] = "ArrowUp",
        ["Down"] = "ArrowDown",
    };

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;
    private ICDPSession? _cdp;
    private string _hostPort = "";
    private string _url = "";
    private double _zoom = 1.0;
    private string? _zoomScriptId;
    private bool _closed;

    public event Action<BrowseFrame>? Frame;

    public event Action<string>? Navigated;

    public string Url
    {
        get
        {
            if (_page is { IsClosed: false } page && page.Url.Length > 0)
            {
                _url = page.Url;
            }

            return _url;
        }
    }

    public bool IsAlive => !_closed && _browser is { IsConnected: true } && _page is { IsClosed: false };

    public int ZoomPercent => (int)Math.Round(_zoom * 100);

    public async Task OpenAsync(string url, int width, int height, CancellationToken cancellationToken)
    {
        try
        {
            _playwright = await Playwright.CreateAsync();
        }
        catch (PlaywrightException ex)
        {
            throw new BrowseException($"The host browser driver could not start: {ex.Message}", ex);
        }

        _browser = await LaunchAsync(_playwright);
        _hostPort = new Uri(url).Port.ToString(CultureInfo.InvariantCulture);
        // Scale 1: screencast pixels are CSS pixels, so the pane paints them 1:1.
        _page = await _browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            DeviceScaleFactor = 1,
        });
        _page.FrameNavigated += (_, _) => Navigated?.Invoke(Url);
        try
        {
            await _page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = (float)OpenTimeout.TotalMilliseconds });
        }
        catch (PlaywrightException ex)
        {
            await DisposeAsync();
            throw new BrowseException($"Could not open {url}: {ex.Message}", ex);
        }

        _url = _page.Url.Length > 0 ? _page.Url : url;
        _cdp = await _page.Context.NewCDPSessionAsync(_page);
        _cdp.Event("Page.screencastFrame").OnEvent += (_, frame) => _ = OnScreencastFrameAsync(frame);
        await SetViewportAsync(width, height);
    }

    /// <summary>Edge first; the bundled Chromium only if Edge will not start.</summary>
    private async Task<IBrowser> LaunchAsync(IPlaywright playwright)
    {
        Exception? last = null;
        foreach (var options in new[] { new BrowserTypeLaunchOptions { Channel = "msedge", Headless = true }, new BrowserTypeLaunchOptions { Headless = true } })
        {
            try
            {
                return await playwright.Chromium.LaunchAsync(options);
            }
            catch (PlaywrightException ex)
            {
                last = ex;
                logger.LogWarning("Host browser launch failed ({Channel}): {Message}", options.Channel ?? "chromium", ex.Message);
            }
        }

        playwright.Dispose();
        _playwright = null;
        throw new BrowseException($"Could not start host browser: {last?.Message ?? "Chromium/Edge launch failed"}", last);
    }

    public async Task SetViewportAsync(int width, int height)
    {
        if (_page is null)
        {
            return;
        }

        await _page.SetViewportSizeAsync(width, height);
        await CdpQuietAsync("Emulation.setDeviceMetricsOverride", new()
        {
            ["width"] = width,
            ["height"] = height,
            ["deviceScaleFactor"] = 1,
            ["mobile"] = false,
        });
    }

    public async Task StartScreencastAsync(int width, int height)
    {
        if (_cdp is null || _closed)
        {
            return;
        }

        await CdpQuietAsync("Page.stopScreencast");
        await _cdp.SendAsync("Page.startScreencast", new()
        {
            ["format"] = "jpeg",
            ["quality"] = 55,
            ["maxWidth"] = width,
            ["maxHeight"] = height,
        });
    }

    public Task StopScreencastAsync() => CdpQuietAsync("Page.stopScreencast");

    public Task RevealIfCoveredAsync() => EvaluateQuietAsync(PageScripts.RevealIfCovered);

    public async Task<JsonObject?> HandleAsync(string type, JsonElement message)
    {
        if (_closed || _page is null)
        {
            return null;
        }

        switch (type)
        {
            case "zoom":
                return await SetZoomAsync((int)BrowseMessage.Number(message, "percent", 100));
            case "selection":
                return await SelectionAsync(message);
            case "copy":
                // The page's selection to this pane's clipboard: the host clipboard is no use to a remote client.
                return new JsonObject { ["type"] = "clipboard", ["text"] = await EvaluateQuietAsync<string>(PageScripts.SelectionText) ?? "" };
            case "pointer":
                return await PointerAsync(message);
            case "key":
                await KeyAsync(message);
                return null;
            case "navigate":
                await NavigateAsync(BrowseMessage.Text(message, "url").Trim());
                return null;
            case "back":
                await _page.GoBackAsync(new PageGoBackOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = (float)HistoryTimeout.TotalMilliseconds });
                Navigated?.Invoke(Url);
                return null;
            case "forward":
                await _page.GoForwardAsync(new PageGoForwardOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = (float)HistoryTimeout.TotalMilliseconds });
                Navigated?.Invoke(Url);
                return null;
            case "reload":
                await _page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = (float)HistoryTimeout.TotalMilliseconds });
                Navigated?.Invoke(Url);
                return null;
            default:
                return null;
        }
    }

    private async Task NavigateAsync(string href)
    {
        if (href.Length == 0 || _page is null)
        {
            return;
        }

        if (!BrowseTarget.IsAllowedUrl(href, _hostPort))
        {
            throw new BrowseException($"Navigation limited to http://127.0.0.1:{_hostPort}");
        }

        if (href.StartsWith('/'))
        {
            href = $"http://127.0.0.1:{_hostPort}{href}";
        }

        await _page.GotoAsync(href, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = (float)OpenTimeout.TotalMilliseconds });
        Navigated?.Invoke(Url);
    }

    /// <summary>Clamped to 50–150 %; applied now and to every document the page loads next.</summary>
    private async Task<JsonObject> SetZoomAsync(int percent)
    {
        _zoom = Math.Clamp(percent, 50, 150) / 100.0;
        var value = Math.Abs(_zoom - 1.0) < 0.001 ? "" : _zoom.ToString("0.##", CultureInfo.InvariantCulture);
        await EvaluateQuietAsync($"document.documentElement.style.zoom = '{value}'");
        if (_zoomScriptId is not null)
        {
            await CdpQuietAsync("Page.removeScriptToEvaluateOnNewDocument", new() { ["identifier"] = _zoomScriptId });
            _zoomScriptId = null;
        }

        if (value.Length > 0 && await CdpQuietAsync("Page.addScriptToEvaluateOnNewDocument", new() { ["source"] = PageScripts.Zoom(value) }) is { } added
            && added.TryGetProperty("identifier", out var identifier))
        {
            _zoomScriptId = identifier.GetString();
        }

        return new JsonObject { ["type"] = "zoom", ["percent"] = ZoomPercent };
    }

    /// <summary>
    /// Mouse input. A click and a release answer with what took focus, so the
    /// pane opens the soft keyboard only over a text field; the page itself
    /// never scrolls on a click.
    /// </summary>
    private async Task<JsonObject?> PointerAsync(JsonElement message)
    {
        var page = _page!;
        var x = (float)BrowseMessage.Number(message, "x");
        var y = (float)BrowseMessage.Number(message, "y");
        var button = BrowseMessage.Text(message, "button") switch
        {
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => MouseButton.Left,
        };
        var clicks = Math.Clamp(BrowseMessage.Integer(message, "clickCount") ?? 1, 1, 3);
        switch (BrowseMessage.Text(message, "action") is { Length: > 0 } action ? action : "click")
        {
            case "click":
                await page.Mouse.ClickAsync(x, y, new MouseClickOptions { Button = button, ClickCount = clicks });
                await FocusAtAsync(x, y);
                return await FocusReplyAsync();
            case "wheel":
                // The page scrolls inside the browser, scrollbars and all, and the screencast shows it.
                await page.Mouse.MoveAsync(x, y);
                await page.Mouse.WheelAsync((float)BrowseMessage.Number(message, "deltaX"), (float)BrowseMessage.Number(message, "deltaY"));
                return null;
            case "move":
                await page.Mouse.MoveAsync(x, y);
                return null;
            case "down":
                await page.Mouse.MoveAsync(x, y);
                await page.Mouse.DownAsync(new MouseDownOptions { Button = button, ClickCount = clicks });
                return null;
            case "up":
                await page.Mouse.MoveAsync(x, y);
                await page.Mouse.UpAsync(new MouseUpOptions { Button = button, ClickCount = clicks });
                await FocusAtAsync(x, y);
                return await FocusReplyAsync();
            default:
                return null;
        }
    }

    /// <summary>Forces DOM focus onto what is under the click: a password field does not always take it from the click alone.</summary>
    private async Task FocusAtAsync(float x, float y)
    {
        var hit = await CdpQuietAsync("DOM.getNodeForLocation", new()
        {
            ["x"] = (int)x,
            ["y"] = (int)y,
            ["includeUserAgentShadowDOM"] = true,
        });
        if (hit is { } node && node.TryGetProperty("backendNodeId", out var id) && id.TryGetInt32(out var backendNodeId) && backendNodeId > 0)
        {
            await CdpQuietAsync("DOM.focus", new() { ["backendNodeId"] = backendNodeId });
        }
    }

    private async Task<JsonObject> FocusReplyAsync()
    {
        var info = await EvaluateQuietAsync<JsonElement?>(PageScripts.Focus);
        var editable = info is { ValueKind: JsonValueKind.Object } found && found.TryGetProperty("editable", out var e) && e.ValueKind == JsonValueKind.True;
        var inputType = info is { ValueKind: JsonValueKind.Object } typed && typed.TryGetProperty("inputType", out var t) && t.GetString() is { Length: > 0 } text ? text : "text";
        return new JsonObject { ["type"] = "focus", ["editable"] = editable, ["inputType"] = inputType };
    }

    /// <summary>
    /// The pane's selection handles: <c>get</c> the geometry, <c>anchor</c> the end
    /// a handle drags, <c>extend</c> with a shift+click at the finger.
    /// </summary>
    private async Task<JsonObject> SelectionAsync(JsonElement message)
    {
        var op = BrowseMessage.Text(message, "op") is { Length: > 0 } requested ? requested : "get";
        var handle = BrowseMessage.Text(message, "handle") is "start" or "end" ? BrowseMessage.Text(message, "handle") : null;
        if (op == "extend" && _page is not null)
        {
            try
            {
                await _page.Keyboard.DownAsync("Shift");
                await _page.Mouse.ClickAsync((float)BrowseMessage.Number(message, "x"), (float)BrowseMessage.Number(message, "y"));
            }
            catch (PlaywrightException ex)
            {
                logger.LogDebug("Browse selection extend failed: {Message}", ex.Message);
            }
            finally
            {
                await Quietly(() => _page.Keyboard.UpAsync("Shift"));
            }
        }

        var reply = new JsonObject { ["type"] = "selection" };
        var geometry = await EvaluateQuietAsync<JsonElement?>(PageScripts.Selection, op == "anchor" ? handle : null);
        if (geometry is { ValueKind: JsonValueKind.Object } found && JsonNode.Parse(found.GetRawText()) is JsonObject fields)
        {
            foreach (var (name, value) in fields.ToList())
            {
                fields.Remove(name);
                reply[name] = value;
            }
        }
        else
        {
            reply["empty"] = true;
        }

        return reply;
    }

    /// <summary>Text goes in as one committed insert (as an IME does); named keys and Ctrl combinations as key presses.</summary>
    private async Task KeyAsync(JsonElement message)
    {
        var page = _page!;
        var text = BrowseMessage.Text(message, "text");
        if (text.Length > 0)
        {
            try
            {
                await _cdp!.SendAsync("Input.insertText", new() { ["text"] = text });
            }
            catch (PlaywrightException ex)
            {
                logger.LogWarning("CDP insertText failed: {Message}", ex.Message);
                await Quietly(() => page.Keyboard.InsertTextAsync(text));
            }

            return;
        }

        var key = BrowseMessage.Text(message, "key");
        if (key.Length == 0)
        {
            return;
        }

        var mapped = KeyAliases.GetValueOrDefault(key, key);
        try
        {
            await page.Keyboard.PressAsync(mapped);
        }
        catch (PlaywrightException ex)
        {
            // A key only the client's keyboard has (a media key, an OS key).
            logger.LogDebug("Host browser key ignored ({Key}): {Message}", mapped, ex.Message);
        }
    }

    private async Task OnScreencastFrameAsync(JsonElement? frame)
    {
        if (_closed || _cdp is null || frame is not { ValueKind: JsonValueKind.Object } data)
        {
            return;
        }

        if (data.TryGetProperty("sessionId", out var sessionId))
        {
            try
            {
                // Unacknowledged, the screencast sends no further picture.
                await _cdp.SendAsync("Page.screencastFrameAck", new() { ["sessionId"] = sessionId.GetInt32() });
            }
            catch (PlaywrightException)
            {
                return;
            }
        }

        var jpeg = data.TryGetProperty("data", out var encoded) ? Convert.FromBase64String(encoded.GetString() ?? "") : [];
        var metadata = data.TryGetProperty("metadata", out var meta) ? meta : default;
        Frame?.Invoke(new BrowseFrame(jpeg, Dimension(metadata, "deviceWidth"), Dimension(metadata, "deviceHeight"), Url));
    }

    private static int Dimension(JsonElement metadata, string name) =>
        metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? (int)number : 0;

    /// <summary>A DevTools call whose failure only means the page is navigating or gone.</summary>
    private async Task<JsonElement?> CdpQuietAsync(string method, Dictionary<string, object>? args = null)
    {
        if (_cdp is null)
        {
            return null;
        }

        try
        {
            return await _cdp.SendAsync(method, args);
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    private Task EvaluateQuietAsync(string script) => Quietly(() => _page?.EvaluateAsync(script) ?? Task.CompletedTask);

    private async Task<T?> EvaluateQuietAsync<T>(string script, object? arg = null)
    {
        if (_page is null)
        {
            return default;
        }

        try
        {
            return await _page.EvaluateAsync<T>(script, arg);
        }
        catch (PlaywrightException)
        {
            // The page is navigating or closed: there is nothing to read.
            return default;
        }
    }

    private static async Task Quietly(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (PlaywrightException)
        {
            // Navigating or closed.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        await StopScreencastAsync();
        _cdp = null;
        if (_browser is not null)
        {
            await Quietly(() => _browser.CloseAsync());
            _browser = null;
        }

        _page = null;
        _playwright?.Dispose();
        _playwright = null;
    }
}

/// <summary>Playwright pages for the agent's sessions.</summary>
public sealed class PlaywrightPages(ILoggerFactory loggers) : IBrowserPageFactory
{
    public IBrowserPage Create() => new PlaywrightPage(loggers.CreateLogger<PlaywrightPage>());
}
