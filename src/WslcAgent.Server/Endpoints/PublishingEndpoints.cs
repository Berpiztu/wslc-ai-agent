using Microsoft.AspNetCore.Http.HttpResults;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Publishing;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/publications</c> and <c>/api/v1/publishing/settings</c>: a container port on a public name. See docs/api-v1.md.</summary>
public static class PublishingEndpoints
{
    public static RouteGroupBuilder MapPublishingEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/publishing/settings", (PublishingSettingsStore store) => store.Get())
            .WithName("GetPublishingSettings");

        api.MapPut("/publishing/settings", (PublishingSettings settings, PublishingSettingsStore store) => store.Set(settings))
            .WithName("SetPublishingSettings");

        api.MapPost("/publishing/setup", (IPublishingSetup setup, CancellationToken ct) => setup.SetupAsync(ct))
            .WithName("SetUpPublishing");

        var group = api.MapGroup("/publications");

        group.MapGet("", async (IPublishingService publishing, CancellationToken ct) => new PublicationList(await publishing.ListAsync(ct)))
            .WithName("ListPublications");

        group.MapPost("", async Task<Created<Publication>> (PublishRequest request, IPublishingService publishing, CancellationToken ct) =>
            {
                var publication = await publishing.PublishAsync(request, ct);
                return TypedResults.Created($"/api/v1/publications/{publication.Hostname}", publication);
            })
            .WithName("Publish");

        group.MapDelete("/{hostname}", async Task<NoContent> (string hostname, IPublishingService publishing, CancellationToken ct) =>
            {
                await publishing.UnpublishAsync(hostname, ct);
                return TypedResults.NoContent();
            })
            .WithName("Unpublish");

        return api;
    }
}
