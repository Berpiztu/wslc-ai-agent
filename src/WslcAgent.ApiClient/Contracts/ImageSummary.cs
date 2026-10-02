namespace WslcAgent.ApiClient.Contracts;

/// <summary>One row of <c>GET /api/v1/images</c>.</summary>
/// <param name="Id">Short image id (12 hex characters, no <c>sha256:</c>).</param>
/// <param name="Repository">Repository, empty for an image that never had a name.</param>
/// <param name="Tag">Tag, empty for a dangling image — also one whose tag a pull moved to a newer image.</param>
/// <param name="Reference"><c>repository:tag</c>, or the id for an image without both (see <see cref="IsDangling"/>): what every action takes.</param>
/// <param name="CreatedAt">Creation time as the CLI prints it (sortable).</param>
/// <param name="CreatedSince">Relative creation time, e.g. <c>9 days ago</c>.</param>
/// <param name="Size">Size as the CLI prints it, e.g. <c>5.09GB</c>.</param>
/// <param name="Containers">Containers created from the image, when the CLI reports it.</param>
/// <param name="InUse">Some container (any state) uses the image.</param>
/// <param name="Digest">The manifest digest (<c>sha256:…</c>), empty when the CLI reports none. Needs <c>wslc</c> 2.9.13, which filled a field that was <c>&lt;none&gt;</c> before it.</param>
/// <param name="Uid">The agent's own number for this image, by its <c>repository:tag</c> (the resource registry): what a resource card on the dashboard points at; 0 for a dangling image.</param>
public sealed record ImageSummary(
    string Id,
    string Repository,
    string Tag,
    string Reference,
    string CreatedAt,
    string CreatedSince,
    string Size,
    int? Containers,
    bool InUse,
    string Digest = "",
    int Uid = 0)
{
    /// <summary>
    /// No name that is this image alone: no repository, or a repository whose
    /// tag a pull moved to a newer image, which the repository alone would name.
    /// </summary>
    public bool IsDangling => Repository.Length == 0 || Tag.Length == 0;
}

/// <summary>Totals for the page header.</summary>
public sealed record ImageAggregate(int Count, long SizeBytes);

/// <summary>Body of <c>GET /api/v1/images</c>.</summary>
public sealed record ImageListResponse(IReadOnlyList<ImageSummary> Images, ImageAggregate Aggregate);

/// <summary>Body of <c>POST /api/v1/images/pull</c>: <c>wslc image pull [--all-tags] REFERENCE</c>.</summary>
/// <param name="AllTags">Download every tag of the repository instead of one. Needs <c>wslc</c> 2.9.13.</param>
public sealed record PullImageRequest(string Reference, bool AllTags = false);

/// <summary>Body of <c>POST /api/v1/images/tag</c>: <c>wslc image tag SOURCE TARGET</c>.</summary>
public sealed record TagImageRequest(string Source, string Target);

/// <summary>Body of <c>POST /api/v1/images/push</c>: <c>wslc image push [--all-tags] REFERENCE</c>.</summary>
public sealed record PushImageRequest(string Reference, bool AllTags = false);

/// <summary>Body of <c>POST /api/v1/images/publish</c>: a local image tagged as a version in a registry and pushed there.</summary>
/// <param name="Source">The local image, as listed.</param>
/// <param name="Target">The reference in the registry, with its version: <c>ghcr.io/owner/app:1.4</c>.</param>
/// <param name="Latest">Also tag and push <c>latest</c> of the same repository.</param>
public sealed record PublishImageRequest(string Source, string Target, bool Latest = false);

/// <summary>
/// Body of <c>POST /api/v1/images/save</c>: <c>wslc image save --output PATH REFERENCE</c>.
/// A bare file name lands in the Downloads folder of the user the agent runs as.
/// </summary>
public sealed record SaveImageRequest(string Reference, string Output);

/// <summary>Answer of <c>POST /api/v1/images/save</c>: the archive written on the agent's machine.</summary>
public sealed record SavedImage(string Reference, string Path);
