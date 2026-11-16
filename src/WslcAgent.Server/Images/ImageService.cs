using System.Text.Json;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>Images as <c>wslc image list --format json</c> reports them, each with the containers <c>wslc</c> counts on it: by the image's id, never by its name.</summary>
public sealed class ImageService(IWslcRunner wslc, ImagePulls pulls, Resources.ResourceRegistry registry, ImageMarks marks, ImageVersions? versions = null, ImageIntegrity? integrity = null) : IImageService
{
    private const string None = "<none>";
    private static readonly TimeSpan PullTimeout = TimeSpan.FromMinutes(30);

    public async Task<ImageListResponse> ListAsync(CancellationToken cancellationToken = default)
    {
        // --digests fills the Digest field, which the CLI left as <none> before
        // wslc 2.9.13; without the flag the column is not computed at all.
        var list = await wslc.RunAsync(["image", "list", "--digests", "--format", "json"], cancellationToken: cancellationToken);
        var rows = WslcJson.ParseRows(list.Stdout).Select(ToSummary).ToList();
        // Every read is the registry's too, an image by its repository:tag; a
        // dangling image has no name to keep and is not entered.
        var tagged = rows.Where(i => !i.IsDangling).Select(i => (i.Reference, i.Reference)).ToList();
        var uids = registry.Reconcile(Resources.ResourceRegistry.Image, tagged, complete: true);
        // What Zaindari last found in each, on the image itself (ImageMarks).
        var images = marks.Annotate([.. rows.Select(i => i.IsDangling ? i : i with { Uid = uids.GetValueOrDefault(i.Reference) })]);
        // The version each says it is, read once by its id (ImageVersions).
        if (versions is not null)
        {
            images = await versions.AnnotateAsync(images, cancellationToken);
        }

        var aggregate = new ImageAggregate(images.Count, images.Sum(i => StatsParsing.Bytes(i.Size)));
        return new ImageListResponse(images, aggregate);
    }

    public async Task<ImageInspect> InspectAsync(string reference, CancellationToken cancellationToken = default)
    {
        var result = await wslc.RunAsync(["image", "inspect", WslcArgs.Require(reference, "image reference"), "--format", "json"], cancellationToken: cancellationToken);
        var first = WslcJson.ParseRows(result.Stdout).FirstOrDefault();
        var json = first.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Serialize(first, new JsonSerializerOptions { WriteIndented = true })
            : result.Stdout;
        return new ImageInspect(reference, json);
    }

    /// <summary>
    /// A container row says the image name it was made with, and <c>wslc</c>
    /// counts an image's containers without naming them; which image a
    /// container is pinned to is in its inspect, so each one is inspected and
    /// its image's id compared with this image's.
    /// </summary>
    public async Task<ImageUsers> UsersAsync(string reference, CancellationToken cancellationToken = default)
    {
        var image = WslcArgs.Require(reference, "image reference");
        var inspected = await wslc.RunAsync(["image", "inspect", image, "--format", "json"], cancellationToken: cancellationToken);
        var imageId = PinnedTo(inspected, "Id");
        if (imageId.Length == 0)
        {
            throw new KeyNotFoundException($"Image {image} is not on this session.");
        }

        var list = await wslc.RunAsync(["container", "list", "--all", "--format", "json"], cancellationToken: cancellationToken);
        var containers = WslcJson.ParseRows(list.Stdout)
            .Where(row => !ContainerService.IsHelper(row.GetString("Names")))
            .Select(row => row.GetString("ID") is { Length: > 0 } id ? id : row.GetString("Id"))
            .Where(id => id.Length > 0)
            .ToList();


        var users = new List<ImageUser>();
        foreach (var id in containers)
        {
            var result = await wslc.RunAsync(["container", "inspect", id, "--format", "json"], cancellationToken: cancellationToken);
            if (PinnedTo(result, "Image") == imageId)
            {
                var container = ContainerInspection.Parse(result);
                users.Add(new ImageUser(container.Name, container.Id, container.Image, container.State));
            }
        }

        return new ImageUsers(image, imageId[..Math.Min(12, imageId.Length)], [.. users.OrderBy(user => user.Name.Length > 0 ? user.Name : user.Id, StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>An image id out of an inspect, without its algorithm: an image's own <c>Id</c>, or the <c>Image</c> a container is pinned to.</summary>
    private static string PinnedTo(WslcResult inspect, string field)
    {
        var root = WslcJson.ParseRows(inspect.Stdout).FirstOrDefault();
        var id = root.ValueKind == JsonValueKind.Object ? root.GetString(field).Trim() : "";
        return id.StartsWith("sha256:", StringComparison.Ordinal) ? id["sha256:".Length..] : id;
    }

    public async Task<ImageDefaults> DefaultsAsync(string reference, CancellationToken cancellationToken = default)
    {
        // Asked of the catalog first: an image that is not here is an answer, not the CLI's failure.
        var image = WslcArgs.Require(reference, "image reference");
        if (!ContainerLaunches.IsLocal((await ListAsync(cancellationToken)).Images, image))
        {
            return new ImageDefaults(false, new ContainerLaunchRequest { Image = image });
        }

        var result = await wslc.RunAsync(["image", "inspect", image, "--format", "json"], cancellationToken: cancellationToken);
        return new ImageDefaults(true, ContainerInspection.ImageFields(image, result));
    }

    public async Task PullAsync(string reference, bool allTags = false, CancellationToken cancellationToken = default)
    {
        var image = ImageReference.Normalize(WslcArgs.Require(reference, "image reference"));
        await wslc.RunAsync(new List<string> { "image", "pull" }.Flag("--all-tags", allTags).Append(image).ToList(), PullTimeout, cancellationToken);
        // A pull that ended well onto a disk that filled up leaves an image that is listed and does not run.
        if (integrity is not null && await integrity.WhyDamagedAsync(image, cancellationToken) is { } damaged)
        {
            throw new InvalidOperationException(damaged);
        }
    }

    /// <summary>Uploads can take as long as a pull.</summary>
    public Task PushAsync(string reference, bool allTags = false, CancellationToken cancellationToken = default) =>
        wslc.RunAsync(new List<string> { "image", "push" }.Flag("--all-tags", allTags).Append(ImageReference.Normalize(WslcArgs.Require(reference, "image reference"))).ToList(), PullTimeout, cancellationToken);

    public async Task<SavedImage> SaveAsync(string reference, string output, CancellationToken cancellationToken = default)
    {
        var image = WslcArgs.Require(reference, "image reference");
        var path = SavePath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await wslc.RunAsync(["image", "save", "--output", path, image], PullTimeout, cancellationToken);
        return new SavedImage(image, path);
    }

    /// <summary>
    /// Where a save lands. A bare name would otherwise go to the agent's working
    /// directory, its install folder for an installed agent; the user's Downloads
    /// is where someone looks for a file they asked for.
    /// </summary>
    internal static string SavePath(string output)
    {
        var path = WslcArgs.Require(output, "output path");
        return Path.IsPathFullyQualified(path)
            ? path
            : Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", path));
    }

    public Task TagAsync(string source, string target, CancellationToken cancellationToken = default) =>
        wslc.RunAsync(["image", "tag", WslcArgs.Require(source, "source image"), WslcArgs.Require(target, "target reference")], cancellationToken: cancellationToken);

    public async Task RemoveAsync(string reference, bool force, CancellationToken cancellationToken = default)
    {
        await wslc.RunAsync(new List<string> { "image", "remove" }.Flag("--force", force).Append(WslcArgs.Require(reference, "image reference")).ToList(), cancellationToken: cancellationToken);
        registry.Removed(Resources.ResourceRegistry.Image, reference);
    }

    /// <summary>
    /// Dangling images only, as the Images page promises; <paramref name="all"/>
    /// (the System page) every unused one. <c>-f</c> is required: WSL
    /// 2.9.12 prunes ask <c>[y/N]</c> and read the agent's closed stdin as "no"
    /// while still exiting 0, so without it the call removes nothing and reports
    /// success.
    /// </summary>
    public async Task<CommandOutput> PruneAsync(bool all = false, CancellationToken cancellationToken = default) =>
        (await wslc.RunAsync(new List<string> { "image", "prune", "-f" }.Flag("-a", all), cancellationToken: cancellationToken)).Output;

    /// <summary>
    /// Map one row. <c>Repository</c> and <c>Tag</c> print <c>&lt;none&gt;</c> for a
    /// dangling image; its reference is then the id, the only form the CLI accepts.
    /// <c>Containers</c>, the containers <c>wslc</c> counts on the image by its
    /// id, decides usage and nothing else does: a container's image name can
    /// belong to another image by now.
    /// </summary>
    internal static ImageSummary ToSummary(JsonElement row)
    {
        var fullId = ShortId(FirstNonEmpty(row.GetString("ID"), row.GetString("Id")));
        var repository = NoneAsEmpty(row.GetString("Repository"));
        var tag = NoneAsEmpty(row.GetString("Tag"));
        // Only repository:tag names this row and no other; anything less is the
        // id. A pull moves the tag to the new image and leaves the old one with
        // its repository and a <none> tag, and the repository alone is read by
        // wslc as repository:latest — the new image: Remove on the old row
        // tried the new one and was refused as in use.
        var reference = repository.Length > 0 && tag.Length > 0 ? $"{repository}:{tag}" : fullId;
        var containers = int.TryParse(row.GetString("Containers"), out var count) && count >= 0 ? count : (int?)null;
        var inUse = containers is > 0;

        return new ImageSummary(
            Id: fullId,
            Repository: repository,
            Tag: tag,
            Reference: reference,
            CreatedAt: row.GetString("CreatedAt"),
            CreatedSince: row.GetString("CreatedSince"),
            Size: row.GetString("Size"),
            Containers: containers,
            InUse: inUse,
            Digest: NoneAsEmpty(row.GetString("Digest")));
    }

    /// <summary>The pull jobs the agent is running, the same ones its UI draws.</summary>
    public Task<IReadOnlyList<ImagePullState>> PullsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(pulls.List());

    /// <summary>
    /// Starts a pull as a job and answers at once. A caller that cannot sit and
    /// wait for a large image asks again through the job instead of starting the
    /// download over, which is what a second pull of the same image would do.
    /// </summary>
    public Task<ImagePullState> StartPullAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(pulls.Start(reference));

    private static string ShortId(string id)
    {
        var text = id.StartsWith("sha256:", StringComparison.Ordinal) ? id["sha256:".Length..] : id;
        return text.Length > 12 ? text[..12] : text;
    }

    private static string NoneAsEmpty(string value) => value.Trim() == None ? "" : value.Trim();

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
}
