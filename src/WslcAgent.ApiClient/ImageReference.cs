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
    /// </summary>
    public static string Normalize(string image)
    {
        var (repository, tag, digest) = Parts(image.Trim());
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
