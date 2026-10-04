using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

public sealed class LogClearingStoreTests
{
    private static LogClearingStore Store(string dataDirectory) =>
        new(Options.Create(new WslcOptions { DataDirectory = dataDirectory }), NullLogger<LogClearingStore>.Instance);

    [Fact]
    public void A_clear_holds_after_the_agent_restarts()
    {
        var data = TestHost.TempDataDirectory();
        var at = new DateTimeOffset(2026, 10, 4, 22, 30, 15, TimeSpan.Zero);

        Store(data).Set("web", at);

        Assert.Equal(at, Store(data).Get("web"));
        Assert.Equal(at, Store(data).Get("/WEB"));
    }

    [Fact]
    public void Removing_the_clear_shows_the_history_whole()
    {
        var store = Store(TestHost.TempDataDirectory());
        store.Set("web", DateTimeOffset.UtcNow);

        store.Remove("web");

        Assert.Null(store.Get("web"));
        Assert.Null(store.Get("never-cleared"));
    }
}
