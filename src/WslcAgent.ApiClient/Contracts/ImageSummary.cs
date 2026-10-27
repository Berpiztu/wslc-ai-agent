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
/// <param name="PatchingTo">The patch being built from this image (<c>/image-security/patch</c>), while it is; null otherwise.</param>
/// <param name="PatchJob">That patch's build job (<c>/images/build/{job}</c>), its console and its cancel; null otherwise.</param>
/// <param name="ImageWorst">The worst severity of its vulnerabilities moderate or worse that something can be done about (a fix its upgrade reaches, or a Python library), as Zaindari last found them in this image (by its id, so two tags of two images are two marks): <c>moderate</c>, <c>high</c> or <c>critical</c>; empty for none, or not checked. What raises the alarm.</param>
/// <param name="ImageUnfixed">The worst severity of its vulnerabilities moderate or worse that nobody downstream can mend, a system package its distribution has not fixed for it; said, never an alarm; empty for none.</param>
/// <param name="ImageRefused">The agent makes no new container from it: built here, a vulnerability moderate or worse, Settings → Security set to Block.</param>
/// <param name="ImageCheckedAt">When Zaindari last checked it (<c>yyyy-MM-dd HH:mm</c>, the agent's time); empty with no mark.</param>
/// <param name="ImageVersion">The version it says it is: its <c>org.opencontainers.image.version</c> label, or else the one <c>*_VERSION</c> variable it sets (<see cref="ImageVersionFrom"/>); either may be the image it was built on's. Empty when it says none.</param>
/// <param name="ImageVersionFrom">The variable <see cref="ImageVersion"/> was read from (<c>REDIS_VERSION</c>); empty when it is the label's, or there is none.</param>
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
    int Uid = 0,
    string ImageWorst = "",
    bool ImageRefused = false,
    string ImageCheckedAt = "",
    string? PatchingTo = null,
    string? PatchJob = null,
    string ImageUnfixed = "",
    string ImageVersion = "",
    string ImageVersionFrom = "")
{
    /// <summary>
    /// No name that is this image alone: no repository, or a repository whose
    /// tag a pull moved to a newer image, which the repository alone would name.
    /// </summary>
    public bool IsDangling => Repository.Length == 0 || Tag.Length == 0;
}

/// <summary>
/// Body of <c>GET /api/v1/images/registry-version?image=</c>: the version an
/// image not on the machine says it is, read from its registry before it is
/// pulled, as a pulled one is read (<see cref="ImageSummary.ImageVersion"/>).
/// </summary>
/// <param name="Image">The reference asked about, as the agent reads it.</param>
/// <param name="Version">The version the image says it is: its label, or else its one <c>*_VERSION</c> variable; empty when it says none, or the registry did not say.</param>
/// <param name="From">The variable <see cref="Version"/> was read from; empty for the label.</param>
/// <param name="Note">Why there is no version, in words (the image says none, the registry asks for a login, it could not be reached); empty when there is one.</param>
public sealed record RegistryImageVersion(string Image, string Version, string From, string Note);

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
