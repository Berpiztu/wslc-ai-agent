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

        group.MapPost("/{hostname}/check", (string hostname, PublishingService publishing, CancellationToken ct) => publishing.CheckAsync(hostname, ct))
            .WithName("CheckPublication");

        group.MapPut("/{hostname}/force-login", (string hostname, ForceLoginRequest request, PublishingService publishing, CancellationToken ct) =>
                publishing.SetForceLoginAsync(hostname, request.Force, ct))
            .WithName("ForcePublicationLogin");

        // The proxy plugin's own page and API (every name, its users and their passwords,
        // the claim codes), shown inside the agent: passed through as they come, the
        // plugin's admin token added on the way. So it is reached only through the agent,
        // behind its login. No MCP tool on purpose: passwords never go through an assistant.
        api.MapMethods("/publishing/plugin/{**path}", ["GET", "POST", "PUT", "DELETE"], PassToPluginAsync)
            .WithName("PublishingPlugin");

        return api;
    }

    private static async Task PassToPluginAsync(HttpContext http, string? path, IPublishedPlugin plugin)
    {
        var request = new HttpRequestMessage(new HttpMethod(http.Request.Method), (path ?? "") + http.Request.QueryString);
        if (http.Request.ContentLength > 0)
        {
            request.Content = new StreamContent(http.Request.Body);
            if (http.Request.ContentType is { Length: > 0 } type)
            {
                request.Content.Headers.TryAddWithoutValidation("Content-Type", type);
            }
        }

        using var response = await plugin.SendAsync(request, http.RequestAborted);
        http.Response.StatusCode = (int)response.StatusCode;
        if (response.Content.Headers.ContentType is { } contentType)
        {
            http.Response.ContentType = contentType.ToString();
        }

        await response.Content.CopyToAsync(http.Response.Body, http.RequestAborted);
    }
}
