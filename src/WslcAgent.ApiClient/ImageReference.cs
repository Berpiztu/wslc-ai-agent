namespace WslcAgent.ApiClient;

/// <summary>
/// An image reference as a user types it — <c>[registry[:port]/]repository[:tag][@digest]</c> —
/// taken apart and put back the one way, for the agent and its UI alike. A tag
/// is what follows the last colon after the last slash, so a registry's port
/// (<c>host:5000/app</c>) is never read as one.
/// </summary>
public static class ImageReference
{
    /// <summary>The tag a reference means when it names none.</summary>
    public const string Latest = "latest";

    /// <summary>What Zaindari's patch adds to the tag of the image it patches: <c>alpine:latest-patched</c>, built on this machine and published nowhere.</summary>
    public const string PatchedSuffix = "-patched";

    /// <summary>Whether the reference names a patch Zaindari built (<see cref="PatchedSuffix"/>).</summary>
    public static bool IsPatched(string image) => Split(image).Tag.EndsWith(PatchedSuffix, StringComparison.Ordinal);

    /// <summary>The image a patch was made from (<c>alpine:latest-patched</c> → <c>alpine:latest</c>); any other reference as it is.</summary>
    public static string Unpatched(string image)
    {
        var (repository, tag) = Split(image);
        return IsPatched(image) ? $"{repository}:{tag[..^PatchedSuffix.Length]}" : image;
    }

    /// <summary>The repository and the tag; <see cref="Latest"/> when none is named, as wslc reads it.</summary>
    public static (string Repository, string Tag) Split(string image)
    {
        var (repository, tag, _) = Parts(image);
        return (repository, tag.Length > 0 ? tag : Latest);
    }

    /// <summary>The repository without its tag.</summary>
    public static string RepositoryOf(string image) => Parts(image).Repository;

    /// <summary>Whether the reference names a tag or a digest of its own.</summary>
    public static bool HasTag(string image) => Parts(image) is { Tag.Length: > 0 } or { Digest.Length: > 0 };

    /// <summary>
    /// The reference trimmed, its registry and repository in lower case: an image
    /// name is lower case by rule and wslc refuses any other, while a phone's
    /// keyboard capitalises the first letter typed.
    /// A tag keeps its case, which may be mixed; a digest is left as it is.
    /// A pasted pull command (<c>docker pull ghcr.io/owner/app:latest</c>, as a
    /// registry's page shows it) gives its last word: a reference has no spaces,
    /// and the whole line passed on made wslc pull an image called "pull".
    /// </summary>
    public static string Normalize(string image)
    {
        var words = image.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var (repository, tag, digest) = Parts(words.Length > 0 ? words[^1] : "");
        return repository.ToLowerInvariant() + (tag.Length > 0 ? ":" + tag : "") + digest;
    }

    private static (string Repository, string Tag, string Digest) Parts(string image)
    {
        var at = image.IndexOf('@');
        var name = at < 0 ? image : image[..at];
        var digest = at < 0 ? "" : image[at..];
        var colon = name.LastIndexOf(':');
        return colon > name.LastIndexOf('/') ? (name[..colon], name[(colon + 1)..], digest) : (name, "", digest);
    }
}
