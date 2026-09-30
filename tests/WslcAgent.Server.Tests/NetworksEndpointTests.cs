using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

public sealed class NetworksEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Usage = "container list --all --format json";

    [Fact]
    public async Task List_merges_inspect_ipam_and_marks_attached_networks()
    {
        var runner = new FakeWslcRunner()
            .Answer("network list --format json", FakeWslcRunner.Fixture("network-list.ndjson"))
            .Answer("network inspect bridge --format json", FakeWslcRunner.Fixture("network-inspect-bridge.json"))
            .Answer("network inspect host --format json", "[]")
            .Answer("network inspect none --format json", "[]")
            .Fail("network inspect appnet --format json", "boom")
            .Answer(Usage, FakeWslcRunner.Fixture("container-list-usage.ndjson"))
            .Answer("container stats --all --format json", FakeWslcRunner.Fixture("container-stats-usage.ndjson"));

        var body = await factory.ClientWith(runner).GetFromJsonAsync<NetworkListResponse>("/api/v1/networks");

        Assert.NotNull(body);
        Assert.Equal(4, body.Count);
        var bridge = body.Networks.Single(n => n.Name == "bridge");
        Assert.Equal("172.17.0.0/16", bridge.Subnet);
        Assert.Equal("172.17.0.1", bridge.Gateway);
        Assert.True(bridge.InUse);
        Assert.Equal((1, 1), (bridge.Containers, bridge.Running));
        Assert.Equal((3_000L, 1_000L), (bridge.NetReceivedBytes, bridge.NetSentBytes));
        Assert.Equal((3_000L, 1_000L), (body.NetReceivedBytes, body.NetSentBytes));
        Assert.False(bridge.AllowsStaticIp);
        var appnet = body.Networks.Single(n => n.Name == "appnet");
        Assert.True(appnet.InUse);
        Assert.True(appnet.Internal);
        Assert.Equal("", appnet.Subnet);
        var host = body.Networks.Single(n => n.Name == "host");
        Assert.False(host.InUse);
        Assert.Equal((0, 0), (host.Containers, host.Running));
        Assert.Equal((0L, 0L), (host.NetReceivedBytes, host.NetSentBytes));
    }

    [Fact]
    public async Task Create_builds_the_flags_from_the_request()
    {
        var runner = new FakeWslcRunner().Answer("network create --subnet 172.28.0.0/16 --internal appnet", "");

        var response = await factory.ClientWith(runner).PostAsJsonAsync("/api/v1/networks", new CreateNetworkRequest("appnet", Subnet: "172.28.0.0/16", Internal: true));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task Connect_passes_a_static_ip_on_user_networks_only()
    {
        var runner = new FakeWslcRunner()
            .Answer("network connect --ip 172.28.0.5 appnet web", "")
            .Answer("network connect bridge web", "")
            .Answer("network disconnect appnet web", "");
        var client = factory.ClientWith(runner);

        var user = await client.PostAsJsonAsync("/api/v1/networks/appnet/connect", new NetworkContainerRequest("web", "172.28.0.5"));
        var builtIn = await client.PostAsJsonAsync("/api/v1/networks/bridge/connect", new NetworkContainerRequest("web", "172.28.0.5"));
        var detached = await client.PostAsJsonAsync("/api/v1/networks/appnet/disconnect", new NetworkContainerRequest("web"));

        Assert.Equal(HttpStatusCode.NoContent, user.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, builtIn.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, detached.StatusCode);
        Assert.Equal(3, runner.Calls.Count);
    }

    [Fact]
    public async Task Delete_and_prune_run_the_matching_commands()
    {
        var runner = new FakeWslcRunner()
            .Answer("network remove appnet", "")
            .Answer("network prune -f", "");
        var client = factory.ClientWith(runner);

        var removed = await client.DeleteAsync("/api/v1/networks/appnet");
        var pruned = await client.PostAsync("/api/v1/networks/prune", content: null);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, pruned.StatusCode);
        Assert.Equal(2, runner.Calls.Count);
    }
}
