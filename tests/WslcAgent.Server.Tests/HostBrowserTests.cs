using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Browse;
using WslcAgent.Server.Containers;

namespace WslcAgent.Server.Tests;

/// <summary>Open with browser from a remote client: what may be browsed, and how host browsers are resumed, joined, capped and reaped.</summary>
public sealed class HostBrowserTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly ContainerInspection.PortBinding[] Binds =
    [
        new("", "3000", "8080/tcp"),
        new("192.168.1.20", "9000", "80/tcp"),
    ];

    [Fact]
    public void Only_a_published_loopback_port_that_is_not_the_agent_is_browsable()
    {
        Assert.Equal("http://127.0.0.1:3000/", BrowseTarget.Resolve(Binds, "3000", agentPort: 8069));
        Assert.Equal("Invalid host port", Assert.Throws<BrowseException>(() => BrowseTarget.Resolve(Binds, "30a0", 8069)).Message);
        Assert.Equal("Refusing to browse the agent listen port", Assert.Throws<BrowseException>(() => BrowseTarget.Resolve(Binds, "3000", 3000)).Message);
        Assert.Equal("Host port is not published on this container", Assert.Throws<BrowseException>(() => BrowseTarget.Resolve(Binds, "4000", 8069)).Message);
        Assert.Equal("Refusing non-loopback published bind", Assert.Throws<BrowseException>(() => BrowseTarget.Resolve(Binds, "9000", 8069)).Message);
    }

    [Theory]
    [InlineData("/login", true)]
    [InlineData("http://127.0.0.1:3000/a", true)]
    [InlineData("http://localhost:3000", true)]
    [InlineData("//evil.example/", false)]
    [InlineData("http://127.0.0.1:3001/", false)]
    [InlineData("https://example.com:3000/", false)]
    [InlineData("file:///C:/", false)]
    public void Navigation_stays_on_the_session_port(string href, bool allowed) =>
        Assert.Equal(allowed, BrowseTarget.IsAllowedUrl(href, "3000"));

    [Fact]
    public async Task Reopening_resumes_the_same_browser_and_joining_adds_a_viewer()
    {
        var (sessions, pages, _) = Registry(maxSessions: 8);
        var key = BrowseTarget.SessionKey("abcdef0123456789", "3000", "local", "viewer-one");

        var first = await sessions.OpenAsync(key, "abcdef0123456789", "local", "viewer-one", "http://127.0.0.1:3000/", 1000, 700, default);
        await first.Session.EndViewAsync(first.Viewer);
        var again = await sessions.OpenAsync(key, "abcdef012345", "local", "viewer-one", "http://127.0.0.1:3000/", 800, 600, default);
        var joined = await sessions.JoinAsync(first.Session.Id, "abcdef012345", 400, 300, default);

        Assert.True(first.Created);
        Assert.False(again.Created);
        Assert.Same(first.Session, again.Session);
        Assert.Same(first.Session, joined.Session);
        Assert.Single(pages.Created);
        Assert.Equal((800, 600), (pages.Created[0].Width, pages.Created[0].Height));
        var listed = Assert.Single(sessions.List("abcdef012345"));
        Assert.Equal(("3000", 2, "local"), (listed.HostPort, listed.Viewers, listed.Client));
        await Assert.ThrowsAsync<BrowseException>(() => sessions.JoinAsync(first.Session.Id, "ffffff012345", 400, 300, default));
    }

    [Fact]
    public async Task A_browser_past_the_cap_is_refused()
    {
        var (sessions, _, _) = Registry(maxSessions: 1);
        await sessions.OpenAsync("a", "c1", "local", "v1", "http://127.0.0.1:3000/", 800, 600, default);

        var refused = await Assert.ThrowsAsync<BrowseException>(() => sessions.OpenAsync("b", "c1", "local", "v2", "http://127.0.0.1:3000/", 800, 600, default));

        Assert.StartsWith("Too many host browser sessions open (1/1)", refused.Message);
    }

    [Fact]
    public async Task A_browser_nobody_watches_is_reaped_after_the_idle_limit()
    {
        var (sessions, pages, clock) = Registry(maxSessions: 8);
        var opened = await sessions.OpenAsync("k", "c1", "local", "v1", "http://127.0.0.1:3000/", 800, 600, default);

        clock.Advance(TimeSpan.FromMinutes(45));
        Assert.Empty(await sessions.ReapAsync());

        await opened.Session.EndViewAsync(opened.Viewer);
        clock.Advance(TimeSpan.FromMinutes(31));
        var reaped = await sessions.ReapAsync();

        Assert.Equal([opened.Session.Id], reaped);
        Assert.True(pages.Created[0].Disposed);
        Assert.Empty(sessions.List());
    }

    [Fact]
    public async Task Frames_reach_each_viewer_newest_first_and_messages_in_order()
    {
        var (sessions, pages, _) = Registry(maxSessions: 8);
        var opened = await sessions.OpenAsync("k", "c1", "local", "v1", "http://127.0.0.1:3000/", 800, 600, default);
        var page = pages.Created[0];

        page.Emit(new BrowseFrame([1], 800, 600, "http://127.0.0.1:3000/"));
        page.Emit(new BrowseFrame([2], 800, 600, "http://127.0.0.1:3000/"));
        page.Navigate("http://127.0.0.1:3000/next");
        using var read = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = opened.Session.EventsAsync(opened.Viewer, read.Token).GetAsyncEnumerator(read.Token);

        Assert.True(await events.MoveNextAsync());
        Assert.Equal([2], Assert.IsType<BrowseFrame>(events.Current).Jpeg);
        Assert.True(await events.MoveNextAsync());
        Assert.Equal("http://127.0.0.1:3000/next", Assert.IsType<JsonObject>(events.Current)["href"]!.GetValue<string>());
        Assert.Equal((1, 1), opened.Session.Stats(opened.Viewer));
    }

    [Fact]
    public async Task Sessions_endpoint_lists_none_on_a_fresh_agent()
    {
        var list = await factory.ClientWith(new FakeWslcRunner()).GetFromJsonAsync<BrowseSessionList>("/api/v1/containers/abc/browse-sessions");

        Assert.NotNull(list);
        Assert.Empty(list.Sessions);
    }

    private static (HostBrowserSessions Sessions, FakePages Pages, ManualClock Clock) Registry(int maxSessions)
    {
        var pages = new FakePages();
        var clock = new ManualClock();
        var options = new StaticOptions(new BrowseOptions { IdleMinutes = 30, MaxSessions = maxSessions });
        return (new HostBrowserSessions(pages, options, clock, NullLogger<HostBrowserSessions>.Instance), pages, clock);
    }

    private sealed class FakePages : IBrowserPageFactory
    {
        public List<FakePage> Created { get; } = [];

        public IBrowserPage Create()
        {
            var page = new FakePage();
            Created.Add(page);
            return page;
        }
    }

    private sealed class FakePage : IBrowserPage
    {
        public string Url { get; private set; } = "";

        public bool IsAlive => !Disposed;

        public bool Disposed { get; private set; }

        public int ZoomPercent => 100;

        public int Width { get; private set; }

        public int Height { get; private set; }

        public event Action<BrowseFrame>? Frame;

        public event Action<string>? Navigated;

        public void Emit(BrowseFrame frame) => Frame?.Invoke(frame);

        public void Navigate(string url)
        {
            Url = url;
            Navigated?.Invoke(url);
        }

        public Task OpenAsync(string url, int width, int height, CancellationToken cancellationToken)
        {
            Url = url;
            return SetViewportAsync(width, height);
        }

        public Task SetViewportAsync(int width, int height)
        {
            (Width, Height) = (width, height);
            return Task.CompletedTask;
        }

        public Task StartScreencastAsync(int width, int height) => Task.CompletedTask;

        public Task StopScreencastAsync() => Task.CompletedTask;

        public Task RevealIfCoveredAsync() => Task.CompletedTask;

        public Task<JsonObject?> HandleAsync(string type, JsonElement message) => Task.FromResult<JsonObject?>(null);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class StaticOptions(BrowseOptions value) : IOptionsMonitor<BrowseOptions>
    {
        public BrowseOptions CurrentValue => value;

        public BrowseOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<BrowseOptions, string?> listener) => null;
    }
}
