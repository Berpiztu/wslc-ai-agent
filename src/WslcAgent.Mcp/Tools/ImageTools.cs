using System.ComponentModel;
using ModelContextProtocol.Server;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp.Tools;

/// <summary>Read and additive tools for images. Remove and prune are destructive and come with the gated tools.</summary>
[McpServerToolType]
public static class ImageTools
{
    [McpServerTool(Name = "list_images", ReadOnly = true)]
    [Description("List WSLC images on the selected session. Each row has id, repository, tag, reference (what other tools take), created, size, the number of containers using it and whether it is in use.")]
    public static async Task<IReadOnlyList<ImageSummary>> ListImages(IImageService images, CancellationToken cancellationToken = default) =>
        (await images.ListAsync(cancellationToken)).Images;

    [McpServerTool(Name = "inspect_image", ReadOnly = true)]
    [Description("The raw wslc image inspect JSON of an image: config, layers, labels, the packages it declares.")]
    public static Task<ImageInspect> InspectImage(
        IImageService images,
        [Description("Image reference or id.")] string reference,
        CancellationToken cancellationToken = default) =>
        images.InspectAsync(reference, cancellationToken);

    [McpServerTool(Name = "pull_image")]
    [Description("Pull an image from its registry (wslc image pull). Slow for large images; returns when the pull is complete.")]
    public static async Task<string> PullImage(
        IImageService images,
        [Description("Image reference, e.g. alpine:latest or ghcr.io/org/app:tag.")] string reference,
        [Description("Pull every tag of the repository instead of the one named.")] bool allTags = false,
        CancellationToken cancellationToken = default)
    {
        await images.PullAsync(reference, allTags, cancellationToken);
        return allTags ? $"pulled every tag of {reference}" : $"pulled {reference}";
    }

    [McpServerTool(Name = "pull_status", ReadOnly = true)]
    [Description("What the agent's image pulls are doing: one entry per image, with its state (running, success, error), percentage and last line. Ask this when a pull was left running rather than starting it again — a second pull of the same image supersedes the first.")]
    public static async Task<object> PullStatus(
        IImageService images,
        [Description("One image reference, or empty for every pull the agent knows about.")] string reference = "",
        CancellationToken cancellationToken = default)
    {
        var pulls = await images.PullsAsync(cancellationToken);
        return reference.Trim() is { Length: > 0 } one
            ? pulls.Where(pull => pull.Image.Equals(one, StringComparison.OrdinalIgnoreCase))
            : pulls;
    }

    [McpServerTool(Name = "remove_image", Destructive = true)]
    [Description("Remove an image (wslc image remove). Destructive: it asks for the user's approval first, through their client's prompt or a confirm token.")]
    public static async Task<object> RemoveImage(
        IImageService images,
        ApprovalGate approvals,
        McpServer? server,
        [Description("Image reference or id.")] string reference,
        [Description("Remove it even when a container uses it or it carries several tags.")] bool force = false,
        [Description("The confirm token from the previous answer, once the user approved.")] string? confirm = null,
        CancellationToken cancellationToken = default)
    {
        if (await approvals.CheckAsync(server, "remove_image", $"remove image {reference}", reference,
                new Dictionary<string, string> { ["image"] = reference, ["force"] = force ? "yes" : "no" },
                confirm, cancellationToken) is { } required)
        {
            return required;
        }

        await images.RemoveAsync(reference, force, cancellationToken);
        return $"removed {reference}";
    }

    [McpServerTool(Name = "prune_images", Destructive = true)]
    [Description("Remove images in bulk (wslc image prune): the dangling ones by default, or with all=true every image no container uses. Destructive and not reversible: it asks for the user's approval first.")]
    public static async Task<object> PruneImages(
        IImageService images,
        ApprovalGate approvals,
        McpServer? server,
        [Description("True: every image no container uses, not only the dangling ones.")] bool all = false,
        [Description("The confirm token from the previous answer, once the user approved.")] string? confirm = null,
        CancellationToken cancellationToken = default)
    {
        var scope = all ? "every image no container uses" : "the dangling images";
        if (await approvals.CheckAsync(server, "prune_images", $"prune {scope}", all ? "images:all" : "images:dangling",
                new Dictionary<string, string> { ["scope"] = scope },
                confirm, cancellationToken) is { } required)
        {
            return required;
        }

        return await images.PruneAsync(all, cancellationToken);
    }

    [McpServerTool(Name = "push_image")]
    [Description("Upload an image to the registry its reference names (wslc image push). Needs a registry login for private registries. Slow for large images.")]
    public static async Task<string> PushImage(
        IImageService images,
        [Description("Image reference, e.g. ghcr.io/org/app:1.2.")] string reference,
        [Description("Push every tag of the repository.")] bool allTags = false,
        CancellationToken cancellationToken = default)
    {
        await images.PushAsync(reference, allTags, cancellationToken);
        return $"pushed {reference}";
    }

    [McpServerTool(Name = "save_image")]
    [Description("Save an image to a tar archive on the agent's machine (wslc image save). A bare file name goes to the Downloads folder of the user the agent runs as.")]
    public static Task<SavedImage> SaveImage(
        IImageService images,
        [Description("Image reference or id.")] string reference,
        [Description("Archive path or file name, e.g. alpine-latest.tar.")] string output,
        CancellationToken cancellationToken = default) =>
        images.SaveAsync(reference, output, cancellationToken);

    [McpServerTool(Name = "tag_image")]
    [Description("Give an existing image another name (wslc image tag SOURCE TARGET). Nothing is removed.")]
    public static async Task<string> TagImage(
        IImageService images,
        [Description("Existing image reference or id.")] string source,
        [Description("New reference, e.g. myapp:1.2.")] string target,
        CancellationToken cancellationToken = default)
    {
        await images.TagAsync(source, target, cancellationToken);
        return $"tagged {source} as {target}";
    }
}
