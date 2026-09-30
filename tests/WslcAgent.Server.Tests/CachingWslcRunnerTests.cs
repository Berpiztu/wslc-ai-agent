using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

/// <summary>The read cache: what it keeps, what it never keeps, and what empties it — a list left stale after a change would go unnoticed.</summary>
public sealed class CachingWslcRunnerTests
{
    private const string Stats = "container stats --all --format json";
    private const string List = "container list --all --format json";

    private static CachingWslcRunner Cache(FakeWslcRunner runner)
    {
        var options = Options.Create(new WslcOptions { DataDirectory = TestHost.TempDataDirectory(), ReadCacheSeconds = 30 });
        return new CachingWslcRunner(runner, new SelectedSession(options), options, NullLogger<CachingWslcRunner>.Instance);
    }

    private static Task<WslcResult> Run(IWslcRunner runner, string commandLine) => runner.RunAsync(commandLine.Split(' '));

    [Fact]
    public async Task Whole_list_reads_are_launched_once_until_something_changes()
    {
        var runner = new FakeWslcRunner().Answer(Stats, "{}").Answer(List, "{}").Answer("container rm --force web", "");
        var cache = Cache(runner);

        await Run(cache, Stats);
        await Run(cache, List);
        await Run(cache, Stats);
        await Run(cache, List);
        Assert.Equal(1, runner.Calls.Count(call => string.Join(' ', call) == Stats));
        Assert.Equal(1, runner.Calls.Count(call => string.Join(' ', call) == List));

        await Run(cache, "container rm --force web");
        await Run(cache, List);
        Assert.Equal(2, runner.Calls.Count(call => string.Join(' ', call) == List));
    }

    [Fact]
    public async Task One_containers_stats_and_other_reads_always_run()
    {
        var runner = new FakeWslcRunner().Answer("container stats web --format json", "{}").Answer("container inspect web --format json", "[]");
        var cache = Cache(runner);

        await Run(cache, "container stats web --format json");
        await Run(cache, "container stats web --format json");
        await Run(cache, "container inspect web --format json");
        await Run(cache, "container inspect web --format json");

        Assert.Equal(4, runner.Calls.Count);
    }
}
