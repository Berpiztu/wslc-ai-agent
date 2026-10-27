namespace WslcAgent.UI.Components;

/// <summary>
/// How the version an image says it is reads, wherever it is shown (a
/// container's card, an image's, Update image): <c>1.23</c> from its label;
/// <c>7.4.11 (RedisVer)</c> from a variable, named short, since it may be the
/// image it was built on's (<c>3.12.16 (PyVer)</c>); after a tag, in brackets,
/// and not at all when it is the tag itself.
/// </summary>
public static class ImageVersionText
{
    /// <summary>The version alone, with the variable it came from; empty when the image says none.</summary>
    public static string Of(string version, string from) =>
        version.Length == 0 ? "" : from.Length > 0 ? $"{version} ({Short(from)})" : version;

    /// <summary>A tag and, in brackets, the version it is when the image says one the tag does not: <c>latest (1.23)</c>, <c>7-alpine (7.4.11, RedisVer)</c>.</summary>
    public static string WithTag(string tag, string version, string from) =>
        version.Length == 0 || version == tag ? tag
        : from.Length > 0 ? $"{tag} ({version}, {Short(from)})"
        : $"{tag} ({version})";

    /// <summary>
    /// A version variable's name as a card has room for it: what it is the version of, in one word, and
    /// <c>Ver</c> (<c>REDIS_VERSION</c> → <c>RedisVer</c>); Python, the most met, as <c>Py</c> (<c>PyVer</c>).
    /// </summary>
    public static string Short(string variable)
    {
        var of = variable.EndsWith("_VERSION", StringComparison.Ordinal) ? variable[..^"_VERSION".Length] : variable;
        var word = of == "PYTHON" ? "Py"
            : string.Concat(of.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(part => part[..1] + part[1..].ToLowerInvariant()));
        return $"{word}Ver";
    }
}
