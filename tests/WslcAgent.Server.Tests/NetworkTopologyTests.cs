using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Networks;

namespace WslcAgent.Server.Tests;

/// <summary>The network map's data: each container's networks and addresses from its inspect.</summary>
public sealed class NetworkTopologyTests
{
    [Fact]
    public void Attachments_prefer_the_requested_address_and_add_a_named_network_mode()
    {
        using var inspect = System.Text.Json.JsonDocument.Parse("""
            {"NetworkSettings":{"Networks":{
                "appnet":{"IPAMConfig":{"IPv4Address":"172.28.0.9"},"IPAddress":"172.28.0.2"},
                "bridge":{"IPAddress":"172.17.0.3/16"},
                "default":{}}},
             "HostConfig":{"NetworkMode":"mio"}}
            """);

        var attachments = NetworkTopologyReader.Attachments(inspect.RootElement);

        Assert.Equal(
            [new TopologyAttachment("appnet", "172.28.0.9"), new TopologyAttachment("bridge", "172.17.0.3"), new TopologyAttachment("mio", "")],
            attachments);
    }
}
