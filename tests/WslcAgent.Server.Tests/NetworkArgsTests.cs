using System.Text.Json;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Networks;

namespace WslcAgent.Server.Tests;

/// <summary>IPv6 on a network (wslc 3.0.5): asked for at create, and read back for the edit form, which recreates the network.</summary>
public sealed class NetworkArgsTests
{
    [Fact]
    public void Ipv6_is_a_flag_of_the_create()
    {
        Assert.Equal(["network", "create", "--internal", "--ipv6", "appnet"],
            NetworkService.CreateArgs(new CreateNetworkRequest("appnet", Internal: true, Ipv6: true)));
        Assert.Equal(["network", "create", "appnet"], NetworkService.CreateArgs(new CreateNetworkRequest("appnet")));
    }

    [Fact]
    public void Ipv6_is_read_back_from_inspect()
    {
        using var inspect = JsonDocument.Parse("""{"Name":"appnet","Driver":"bridge","EnableIPv6":true}""");

        Assert.True(NetworkService.FormOf(inspect.RootElement, "appnet").Ipv6);
    }
}
