using System.Text.Json;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>Images as <c>wslc image list --format json</c> reports them, marked with container usage.</summary>
public sealed class ImageService(IWslcRunner wslc, ContainerUsageScanner usage, ImagePulls pulls, Resources.ResourceRegistry registry) : IImageService
{
    private const string None = "<none>";
    private static readonly TimeSpan PullTimeout = TimeSpan.FromMinutes(30);

    public async Task<ImageListResponse> ListAsync(CancellationToken cancellationToken = default)
    {
        // --digests fills the Digest field, which the CLI left as <none> before
        // wslc 2.9.13; without the flag the column is not computed at all.
        var listTask = wslc.RunAsync(["image", "list", "--digests", "--format", "json"], cancellationToken: cancellationToken);
        var usageTask = usage.ScanAsync(cancellationToken);
        await Task.WhenAll(listTask, usageTask);

        var rows = WslcJson.ParseRows(listTask.Result.Stdout)
            .Select(row => ToSummary(row, usageTask.Result))
            .ToList();
        // Every read is the registry's too, an image by its repository:tag; a
        // dangling image has no name to keep and is not entered.
        var tagged = rows.Where(i => !i.IsDangling).Select(i => (i.Reference, i.Reference)).ToList();
        var uids = registry.Reconcile(Resources.ResourceRegistry.Image, tagged, complete: true);
        var images = rows.Select(i => i.IsDangling ? i : i with { Uid = uids.GetValueOrDefault(i.Reference) }).ToList();
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

    public Task PullAsync(string reference, bool allTags = false, CancellationToken cancellationToken = default) =>
        wslc.RunAsync(new List<string> { "image", "pull" }.Flag("--all-tags", allTags).Append(WslcArgs.Require(reference, "image reference")).ToList(), PullTimeout, cancellationToken);

    /// <summary>Uploads can take as long as a pull.</summary>
    public Task PushAsync(string reference, bool allTags = false, CancellationToken cancellationToken = default) =>
        wslc.RunAsync(new List<string> { "image", "push" }.Flag("--all-tags", allTags).Append(WslcArgs.Require(reference, "image reference")).ToList(), PullTimeout, cancellationToken);

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
    /// <c>Containers</c> (2.9.8+) decides usage; without it the container rows do.
    /// </summary>
    internal static ImageSummary ToSummary(JsonElement row, ContainerUsage usage)
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
        var inUse = containers is int n ? n > 0 : UsageKeys(repository, tag, fullId).Overlaps(usage.ImageRefs);

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

    /// <summary>Every spelling a container row may use for this image.</summary>
    private static HashSet<string> UsageKeys(string repository, string tag, string id)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (repository.Length > 0)
        {
            keys.Add(repository);
            if (tag.Length > 0)
            {
                keys.Add($"{repository}:{tag}");
            }
        }

        if (id.Length > 0)
        {
            keys.Add(id);
        }

        return keys;
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
