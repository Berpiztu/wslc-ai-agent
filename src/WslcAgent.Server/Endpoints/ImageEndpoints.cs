using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Images;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/images</c>: list, pull, push, save, tag, files, build, import, load, remove, prune. See docs/api-v1.md.</summary>
public static class ImageEndpoints
{
    public static RouteGroupBuilder MapImageEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/images");

        group.MapGet("", (IImageService images, CancellationToken ct) => images.ListAsync(ct))
            .WithName("ListImages");

        group.MapGet("/inspect", (string reference, IImageService images, CancellationToken ct) => images.InspectAsync(reference, ct))
            .WithName("InspectImage");

        group.MapGet("/defaults", (string reference, IImageService images, CancellationToken ct) => images.DefaultsAsync(reference, ct))
            .WithName("ImageDefaults");

        group.MapPost("/pull", async Task<NoContent> (PullImageRequest request, IImageService images, CancellationToken ct) =>
            {
                await images.PullAsync(request.Reference, request.AllTags, ct);
                return TypedResults.NoContent();
            })
            .WithName("PullImage");

        // The pulls the agent owns and the Images table follows.
        group.MapGet("/pulls", (ImagePulls pulls) => pulls.List())
            .WithName("ImagePulls");

        group.MapPost("/pulls", (PullImageRequest request, ImagePulls pulls) => pulls.Start(request.Reference, request.AllTags))
            .WithName("StartImagePull");

        group.MapGet("/pulls/log", (string image, ImagePulls pulls) => pulls.Log(image))
            .WithName("ImagePullLog");

        group.MapPost("/pulls/cancel", (PullImageRequest request, ImagePulls pulls) => new CancelImagePullResult(pulls.Cancel(request.Reference)))
            .WithName("CancelImagePull");

        // A failed or cancelled pull is remembered a while; the row's cross forgets it now (409 while it still runs).
        group.MapDelete("/pulls", NoContent (string image, ImagePulls pulls) =>
            {
                pulls.Dismiss(image);
                return TypedResults.NoContent();
            })
            .WithName("DismissImagePull");

        group.MapPost("/push", async Task<NoContent> (PushImageRequest request, IImageService images, CancellationToken ct) =>
            {
                await images.PushAsync(request.Reference, request.AllTags, ct);
                return TypedResults.NoContent();
            })
            .WithName("PushImage");

        group.MapPost("/publish", (PublishImageRequest request, ImagePublisher publisher, CancellationToken ct) => publisher.PublishAsync(request, ct))
            .WithName("PublishImage");

        // The pushes the agent owns, as its pulls: what the push console follows.
        group.MapGet("/pushes", (ImagePushes pushes) => pushes.List())
            .WithName("ImagePushes");

        group.MapPost("/pushes", (PushImageRequest request, ImagePushes pushes) => pushes.Start(request.Reference, request.AllTags))
            .WithName("StartImagePush");

        group.MapGet("/pushes/log", (string image, ImagePushes pushes) => pushes.Log(image))
            .WithName("ImagePushLog");

        group.MapPost("/pushes/cancel", (PushImageRequest request, ImagePushes pushes) => new CancelImagePullResult(pushes.Cancel(request.Reference)))
            .WithName("CancelImagePush");

        group.MapDelete("/pushes", NoContent (string image, ImagePushes pushes) =>
            {
                pushes.Dismiss(image);
                return TypedResults.NoContent();
            })
            .WithName("DismissImagePush");

        group.MapPost("/save", (SaveImageRequest request, IImageService images, CancellationToken ct) => images.SaveAsync(request.Reference, request.Output, ct))
            .WithName("SaveImage");

        // Files of an image: a temporary container of it that the container files endpoints browse.
        group.MapPost("/files-session", (ImageFilesRequest request, FilesHelpers helpers, CancellationToken ct) => helpers.OpenImageAsync(request.Reference, ct))
            .WithName("OpenImageFiles");

        group.MapDelete("/files-session/{container}", async Task<NoContent> (string container, FilesHelpers helpers, CancellationToken ct) =>
            {
                await helpers.CloseImageAsync(container, ct);
                return TypedResults.NoContent();
            })
            .WithName("CloseImageFiles");

        group.MapPost("/tag", async Task<NoContent> (TagImageRequest request, IImageService images, CancellationToken ct) =>
            {
                await images.TagAsync(request.Source, request.Target, ct);
                return TypedResults.NoContent();
            })
            .WithName("TagImage");

        group.MapPost("/build", (BuildImageRequest request, ImageBuilds builds) => builds.Start(request))
            .WithName("StartImageBuild");

        group.MapGet("/build/{job}", (string job, ImageBuilds builds) => builds.Get(job))
            .WithName("ImageBuild");

        group.MapPost("/build/{job}/cancel", async Task<NoContent> (string job, ImageBuilds builds) =>
            {
                await builds.CancelAsync(job);
                return TypedResults.NoContent();
            })
            .WithName("CancelImageBuild");

        // Archives run to gigabytes: no request size limit.
        group.MapPost("/import", async Task<NoContent> (IFormFile file, ImageArchives archives, string image = "", CancellationToken ct = default) =>
            {
                await using var content = file.OpenReadStream();
                await archives.ImportAsync(content, file.FileName, image, ct);
                return TypedResults.NoContent();
            })
            .WithName("ImportImage")
            .DisableAntiforgery()
            .WithMetadata(new DisableRequestSizeLimitAttribute())
            .WithFormOptions(multipartBodyLengthLimit: long.MaxValue);

        group.MapPost("/load", async (IFormFile file, ImageArchives archives, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                return await archives.LoadAsync(content, file.FileName, ct);
            })
            .WithName("LoadImages")
            .DisableAntiforgery()
            .WithMetadata(new DisableRequestSizeLimitAttribute())
            .WithFormOptions(multipartBodyLengthLimit: long.MaxValue);

        group.MapPost("/prune", async Task<NoContent> (IImageService images, CancellationToken ct) =>
            {
                await images.PruneAsync(cancellationToken: ct);
                return TypedResults.NoContent();
            })
            .WithName("PruneImages");

        // The reference goes in the query: it holds slashes and colons (ghcr.io/org/app:tag).
        group.MapDelete("", async Task<NoContent> (string reference, IImageService images, bool force = false, CancellationToken ct = default) =>
            {
                await images.RemoveAsync(reference, force, ct);
                return TypedResults.NoContent();
            })
            .WithName("RemoveImage");

        return api;
    }
}
