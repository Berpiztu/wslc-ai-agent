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

    /// <summary>
    /// What Zaindari's patch adds to the tag of the image it patches, built on
    /// this machine and published nowhere: <c>alpine:latest-patched</c> for a
    /// patch of the image alone, and <c>alpine:latest-patched-web</c> for the
    /// one made for the container <c>web</c>. A container's patch is its own:
    /// what was upgraded, raised or accepted in it was chosen for that
    /// container, so another container's patch never takes its name.
    /// </summary>
    public const string PatchedSuffix = "-patched";

    /// <summary>The longest tag a registry, and wslc, take.</summary>
    private const int MaxTagLength = 128;

    /// <summary>Whether the reference names a patch Zaindari built: its tag ends with <see cref="PatchedSuffix"/>, or goes on from it with a container's name.</summary>
    public static bool IsPatched(string image) => PatchedAt(Split(image).Tag) >= 0;

    /// <summary>The image a patch was made from (<c>alpine:latest-patched</c> and <c>alpine:latest-patched-web</c> → <c>alpine:latest</c>); any other reference as it is.</summary>
    public static string Unpatched(string image)
    {
        var (repository, tag) = Split(image);
        return PatchedAt(tag) is >= 0 and var at ? $"{repository}:{tag[..at]}" : image;
    }

    /// <summary>
    /// The patch of <paramref name="image"/> made for <paramref name="container"/>:
    /// the image's own tag, <see cref="PatchedSuffix"/> and the container's name as
    /// a tag takes it, in lower case. Of a patch, it is the patch of the image
    /// that one was made from, for this container.
    /// </summary>
    public static string PatchedFor(string image, string container)
    {
        var (repository, tag) = Split(Unpatched(image));
        var name = string.Concat(container.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' ? c : '-'));
        var patched = $"{tag}{PatchedSuffix}";
        return name.Length == 0
            ? $"{repository}:{patched}"
            : $"{repository}:{patched}-{name[..Math.Min(name.Length, Math.Max(1, MaxTagLength - patched.Length - 1))]}";
    }

    /// <summary>Where the patch's mark starts in a tag: at its end, or before a container's name; -1 for a tag that is no patch's.</summary>
    private static int PatchedAt(string tag)
    {
        var at = tag.LastIndexOf(PatchedSuffix, StringComparison.Ordinal);
        var after = at + PatchedSuffix.Length;
        return at > 0 && (after == tag.Length || tag[after] == '-') ? at : -1;
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
