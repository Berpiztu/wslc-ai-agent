using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace WslcAgent.Server.Browse;

/// <summary>
/// One host browser that outlives the panes watching it. Several panes may be
/// attached at once (the owner, and whoever joined from View browser
/// sessions): every one gets the page's messages and its newest picture.
/// Closing a pane only detaches it; the browser goes when its owner
/// disconnects, the reaper finds it idle, or it dies.
/// </summary>
public sealed class HostBrowserSession(IBrowserPage page, TimeProvider clock) : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<int, Viewer> _viewers = [];
    private int _nextViewer;
    private bool _screencastOn;
    private bool _closed;
    private DateTimeOffset _lastViewerLeft = clock.GetUtcNow();

    public string Id { get; } = BrowseTarget.Token(8);

    public string ContainerId { get; init; } = "";

    /// <summary>Who opened it: <c>local</c>, or the remote address.</summary>
    public string Client { get; init; } = "";

    public string OwnerViewerId { get; init; } = "";

    public string RegistryKey { get; set; } = "";

    public string HostPort { get; private set; } = "";

    public DateTimeOffset Created { get; } = clock.GetUtcNow();

    public int ViewportWidth { get; private set; } = 960;

    public int ViewportHeight { get; private set; } = 640;

    public string Url => page.Url;

    public int ZoomPercent => page.ZoomPercent;

    public bool IsAlive => !_closed && page.IsAlive;

    public bool IsScreencasting => _screencastOn;

    public int ViewerCount
    {
        get
        {
            lock (_gate)
            {
                return _viewers.Count;
            }
        }
    }

    /// <summary>How long it has had no pane attached; zero while someone watches.</summary>
    public TimeSpan IdleFor(DateTimeOffset now)
    {
        lock (_gate)
        {
            return _viewers.Count > 0 ? TimeSpan.Zero : now - _lastViewerLeft;
        }
    }

    /// <summary>Launches the browser at <paramref name="url"/>, attaches the opening pane and starts the screencast; the pane's viewer token.</summary>
    public async Task<int> StartAsync(string url, int width, int height, CancellationToken cancellationToken)
    {
        HostPort = new Uri(url).Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        page.Frame += BroadcastFrame;
        page.Navigated += href => Broadcast(new JsonObject { ["type"] = "url", ["href"] = href });
        await page.OpenAsync(url, width, height, cancellationToken);
        ViewportWidth = width;
        ViewportHeight = height;
        var viewer = BeginView();
        await StartScreencastAsync(width, height);
        return viewer;
    }

    /// <summary>Attaches a pane; its token reads <see cref="EventsAsync"/> and ends with <see cref="EndViewAsync"/>.</summary>
    public int BeginView()
    {
        lock (_gate)
        {
            var token = ++_nextViewer;
            _viewers[token] = new Viewer();
            return token;
        }
    }

    /// <summary>Refits the browser to a resuming pane: a phone-sized page must not come back letterboxed on a desktop.</summary>
    public async Task RefitAsync(int width, int height)
    {
        await SetViewportAsync(width, height);
        await StartScreencastAsync(width, height);
    }

    /// <summary>A joining pane restarts the screencast only if nobody else kept it running.</summary>
    public Task EnsureScreencastAsync(int width, int height) =>
        _screencastOn ? Task.CompletedTask : StartScreencastAsync(width > 0 ? width : ViewportWidth, height > 0 ? height : ViewportHeight);

    /// <summary>Detaches one pane and keeps the browser; the screencast stops with the last one.</summary>
    public async Task EndViewAsync(int viewer)
    {
        bool last;
        lock (_gate)
        {
            if (_viewers.Remove(viewer, out var gone))
            {
                gone.Queue.Writer.TryComplete();
            }

            last = _viewers.Count == 0;
            if (last)
            {
                _lastViewerLeft = clock.GetUtcNow();
            }
        }

        if (last && _screencastOn)
        {
            _screencastOn = false;
            await page.StopScreencastAsync();
        }
    }

    /// <summary>Frames sent and dropped (replaced by a newer one before the pane took it) for one pane.</summary>
    public (int Sent, int Dropped) Stats(int viewer)
    {
        lock (_gate)
        {
            return _viewers.TryGetValue(viewer, out var found) ? (found.Sent, found.Dropped) : (0, 0);
        }
    }

    /// <summary>What one pane is sent, in order: page messages as <see cref="JsonObject"/>, pictures as <see cref="BrowseFrame"/>.</summary>
    public async IAsyncEnumerable<object> EventsAsync(int viewer, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Viewer? found;
        lock (_gate)
        {
            _viewers.TryGetValue(viewer, out found);
        }

        if (found is null)
        {
            yield break;
        }

        await foreach (var item in found.Queue.Reader.ReadAllAsync(cancellationToken))
        {
            if (item is not FrameSlot)
            {
                yield return item;
                continue;
            }

            BrowseFrame? frame;
            lock (_gate)
            {
                frame = found.Frame;
                found.Frame = null;
                if (frame is not null)
                {
                    found.Sent++;
                }
            }

            if (frame is not null)
            {
                yield return frame;
            }
        }
    }

    /// <summary>Drives the page from one pane message; a returned object is that pane's reply.</summary>
    public async Task<JsonObject?> HandleAsync(string type, JsonElement message)
    {
        if (_closed)
        {
            return null;
        }

        if (type != "resize")
        {
            return await page.HandleAsync(type, message);
        }

        var width = BrowseTarget.Width(BrowseMessage.Integer(message, "width"), ViewportWidth);
        var height = BrowseTarget.Height(BrowseMessage.Integer(message, "height"), ViewportHeight);
        await SetViewportAsync(width, height);
        if (ViewerCount > 0)
        {
            await StartScreencastAsync(width, height);
        }

        await page.RevealIfCoveredAsync();
        return null;
    }

    private async Task SetViewportAsync(int width, int height)
    {
        await page.SetViewportAsync(width, height);
        ViewportWidth = width;
        ViewportHeight = height;
    }

    private async Task StartScreencastAsync(int width, int height)
    {
        if (_closed)
        {
            return;
        }

        await page.StartScreencastAsync(width, height);
        _screencastOn = true;
    }

    private void Broadcast(JsonObject message)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            foreach (var viewer in _viewers.Values)
            {
                viewer.Queue.Writer.TryWrite(message);
            }
        }
    }

    /// <summary>
    /// Newest picture only: a frame that arrives while a pane has not taken the
    /// previous one replaces it, so a slow client or tunnel sees the latest
    /// page instead of a growing backlog.
    /// </summary>
    private void BroadcastFrame(BrowseFrame frame)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            foreach (var viewer in _viewers.Values)
            {
                if (viewer.Frame is not null)
                {
                    viewer.Dropped++;
                    viewer.Frame = frame;
                    continue;
                }

                viewer.Frame = frame;
                viewer.Queue.Writer.TryWrite(FrameSlot.Instance);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            foreach (var viewer in _viewers.Values)
            {
                viewer.Queue.Writer.TryComplete();
            }

            _viewers.Clear();
        }

        _screencastOn = false;
        await page.DisposeAsync();
    }

    /// <summary>One attached pane: its ordered queue and its single latest-frame slot.</summary>
    private sealed class Viewer
    {
        public Channel<object> Queue { get; } = Channel.CreateUnbounded<object>(new UnboundedChannelOptions { SingleReader = true });

        public BrowseFrame? Frame { get; set; }

        public int Sent { get; set; }

        public int Dropped { get; set; }
    }

    /// <summary>Marks where in the queue a picture is due; the picture itself is whatever is newest when the pane gets there.</summary>
    private sealed class FrameSlot
    {
        public static readonly FrameSlot Instance = new();
    }
}
