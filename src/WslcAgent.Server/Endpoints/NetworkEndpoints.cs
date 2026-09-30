using Microsoft.AspNetCore.Http.HttpResults;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Networks;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/networks</c>: list, create, connect, disconnect, remove, prune. See docs/api-v1.md.</summary>
public static class NetworkEndpoints
{
    public static RouteGroupBuilder MapNetworkEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/networks");

        group.MapGet("", (INetworkService networks, CancellationToken ct) => networks.ListAsync(ct))
            .WithName("ListNetworks");

        group.MapPost("", async Task<NoContent> (CreateNetworkRequest request, INetworkService networks, CancellationToken ct) =>
            {
                await networks.CreateAsync(request, ct);
                return TypedResults.NoContent();
            })
            .WithName("CreateNetwork");

        group.MapGet("/topology", (NetworkTopologyReader topology, CancellationToken ct) => topology.ReadAsync(ct))
            .WithName("NetworkTopology");

        group.MapGet("/{name}/details", (string name, INetworkService networks, CancellationToken ct) => networks.DetailsAsync(name, ct))
            .WithName("NetworkDetails");

        group.MapPost("/{name}/recreate", (string name, CreateNetworkRequest request, INetworkService networks, CancellationToken ct) => networks.RecreateAsync(name, request, ct))
            .WithName("RecreateNetwork");

        group.MapPost("/prune", async Task<NoContent> (INetworkService networks, CancellationToken ct) =>
            {
                await networks.PruneAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("PruneNetworks");

        group.MapPost("/{name}/connect", async Task<NoContent> (string name, NetworkContainerRequest request, INetworkService networks, CancellationToken ct) =>
            {
                await networks.ConnectAsync(name, request.Container, request.Ip, ct);
                return TypedResults.NoContent();
            })
            .WithName("ConnectNetwork");

        group.MapPost("/{name}/disconnect", async Task<NoContent> (string name, NetworkContainerRequest request, INetworkService networks, CancellationToken ct) =>
            {
                await networks.DisconnectAsync(name, request.Container, ct);
                return TypedResults.NoContent();
            })
            .WithName("DisconnectNetwork");

        group.MapDelete("/{name}", async Task<NoContent> (string name, INetworkService networks, CancellationToken ct) =>
            {
                await networks.RemoveAsync(name, ct);
                return TypedResults.NoContent();
            })
            .WithName("RemoveNetwork");

        return api;
    }
}
