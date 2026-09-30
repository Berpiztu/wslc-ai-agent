namespace WslcAgent.ApiClient;

/// <summary>
/// Paths inside a container: always absolute, always cleaned. Every path a
/// user sends passes through here before it reaches a command, so <c>..</c>
/// cannot climb out, an empty value is the root and a name can never be read
/// as an option.
/// </summary>
public static class ContainerPath
{
    public static string Clean(string? path)
    {
        var raw = (path ?? "/").Trim();
        if (raw.Contains('\0'))
        {
            throw new ArgumentException("A path cannot contain a null character.", nameof(path));
        }

        var parts = new List<string>();
        foreach (var segment in raw.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }

                continue;
            }

            parts.Add(segment);
        }

        return parts.Count == 0 ? "/" : "/" + string.Join('/', parts);
    }

    /// <summary>A path that must name something, not the root: removing or renaming <c>/</c> is refused.</summary>
    public static string File(string? path)
    {
        var clean = Clean(path);
        return clean == "/" ? throw new ArgumentException("The container root is not a file.", nameof(path)) : clean;
    }

    public static string Parent(string path)
    {
        var clean = Clean(path);
        var slash = clean.LastIndexOf('/');
        return slash <= 0 ? "/" : clean[..slash];
    }

    /// <summary>The last segment, with any directory a client may have sent dropped.</summary>
    public static string Name(string path)
    {
        var clean = Clean(path);
        return clean == "/" ? "" : clean[(clean.LastIndexOf('/') + 1)..];
    }

    public static string Join(string directory, string name)
    {
        var parent = Clean(directory);
        return parent == "/" ? $"/{name}" : $"{parent}/{name}";
    }
}
