using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Tests;

/// <summary>View &amp; edit on a network: its details as a form, and Save replacing it with its containers reconnected.</summary>
public sealed class NetworkRecreateTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Inspect = """
        [{"Name":"appnet","Id":"aaaaaaaaaaaabbbbbbbb","Scope":"local","Created":"2026-09-01","Driver":"bridge","Internal":true,
          "IPAM":{"Config":[{"Subnet":"172.28.0.0/16","Gateway":"172.28.0.1"}]},
          "Labels":{"env":"prod","team":"a"},"Options":{},
          "Containers":{"x":{"Name":"web","IPv4Address":"172.28.0.2/16"}}}]
        """;

    [Fact]
    public async Task Details_carry_the_create_time_settings_and_the_attached_containers()
    {
        var runner = new FakeWslcRunner().Answer("network inspect appnet --format json", Inspect);

        var details = await factory.ClientWith(runner).GetFromJsonAsync<NetworkDetails>("/api/v1/networks/appnet/details");

        Assert.NotNull(details);
        Assert.False(details.BuiltIn);
        Assert.Equal(["web (172.28.0.2/16)"], details.Containers);
        Assert.Equal(new CreateNetworkRequest("appnet", "bridge", "172.28.0.0/16", "172.28.0.1", "", true, "env=prod, team=a", ""), details.Form);
    }

    [Fact]
    public async Task Save_under_the_same_name_disconnects_replaces_and_reconnects()
    {
        var runner = new FakeWslcRunner()
            .Answer("network inspect appnet --format json", Inspect)
            .Answer("container list --all --format json", FakeWslcRunner.Fixture("container-list-usage.ndjson"))
            .Answer("container stats --all --format json", "")
            .Answer("network disconnect appnet web", "")
            .Answer("network remove appnet", "")
            .Answer("network create --driver bridge --subnet 10.9.0.0/16 --label env=prod --label team=b appnet", "")
            .Answer("network connect appnet web", "");

        var response = await factory.ClientWith(runner).PostAsJsonAsync("/api/v1/networks/appnet/recreate",
            new CreateNetworkRequest("appnet", "bridge", "10.9.0.0/16", Label: "env=prod, team=b"));
        var result = await response.Content.ReadFromJsonAsync<RecreateNetworkResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(1, result.Reattached);
        Assert.Empty(result.ConnectFailed);
        Assert.Equal(
            ["network disconnect appnet web", "network remove appnet", "network create --driver bridge --subnet 10.9.0.0/16 --label env=prod --label team=b appnet", "network connect appnet web"],
            runner.Calls.Select(c => string.Join(' ', c)).Where(c => c.StartsWith("network ") && !c.StartsWith("network inspect")));
    }

    [Fact]
    public async Task Built_in_networks_are_never_recreated()
    {
        var response = await factory.ClientWith(new FakeWslcRunner()).PostAsJsonAsync("/api/v1/networks/bridge/recreate", new CreateNetworkRequest("bridge"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
