using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Tests;

/// <summary>Home's metrics sum the containers' stats rows; a failing probe is reported, not thrown.</summary>
public sealed class HomeEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Stats = "container stats --all --format json";

    [Fact]
    public async Task Runtime_and_io_sum_every_container()
    {
        var runner = new FakeWslcRunner().Answer(Stats, """
            {"ID":"a","Name":"web","CPUPerc":"1.50%","MemUsage":"100MiB / 7GiB","BlockIO":"1MB / 2MB","NetIO":"3kB / 4kB"}
            {"ID":"b","Name":"db","CPUPerc":"0.25%","MemUsage":"50MiB / 7GiB","BlockIO":"1MB / 0B","NetIO":"1kB / 1kB"}
            """);
        var client = factory.ClientWith(runner);

        var runtime = await client.GetFromJsonAsync<HomeRuntime>("/api/v1/home/metrics/runtime");
        var io = await client.GetFromJsonAsync<HomeIo>("/api/v1/home/metrics/io");

        Assert.Equal(1.75, runtime!.CpuUsedPercent);
        Assert.Equal(150 * 1024 * 1024, runtime.MemoryUsedBytes);
        Assert.Equal((2_000_000, 2_000_000, 4_000, 5_000), (io!.DiskReadBytes, io.DiskWriteBytes, io.NetworkReceivedBytes, io.NetworkSentBytes));
    }

    [Fact]
    public async Task A_failing_stats_call_is_an_error_sample_not_a_failure()
    {
        var runner = new FakeWslcRunner().Fail(Stats, "boom");

        var runtime = await factory.ClientWith(runner).GetFromJsonAsync<HomeRuntime>("/api/v1/home/metrics/runtime");

        Assert.True(runtime!.Error);
    }

    [Fact]
    public async Task The_dashboard_is_kept_as_written_and_a_user_with_none_is_given_the_default()
    {
        var client = factory.ClientWith(new FakeWslcRunner());
        const string layout = """{"version":1,"columns":24,"objects":[]}""";

        var shipped = await client.GetStringAsync("/api/v1/dashboard-v2/default");
        await client.PutAsync("/api/v1/me/dashboard-v2", new StringContent(""));
        var none = await client.GetStringAsync("/api/v1/me/dashboard-v2");
        await client.PutAsync("/api/v1/me/dashboard-v2", new StringContent(layout));
        var kept = await client.GetStringAsync("/api/v1/me/dashboard-v2");
        await client.PutAsync("/api/v1/me/dashboard-v2", new StringContent(""));
        var forgotten = await client.GetStringAsync("/api/v1/me/dashboard-v2");

        Assert.Equal(shipped, none);
        Assert.Equal(layout, kept);
        Assert.Equal(shipped, forgotten);
    }
}
