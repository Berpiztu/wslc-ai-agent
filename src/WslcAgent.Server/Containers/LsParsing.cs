using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Containers;

/// <summary>
/// <c>ls -la</c> output as entries. There is no file API in <c>wslc</c>, so
/// the listing is whatever the container's own shell prints, and this is where
/// it becomes data.
/// </summary>
public static class LsParsing
{
    /// <summary>Every entry of one listing, <c>.</c>, <c>..</c> and the <c>total</c> line left out.</summary>
    public static List<ContainerFileEntry> Entries(string stdout, string directory)
    {
        var entries = new List<ContainerFileEntry>();
        foreach (var line in stdout.Split('\n'))
        {
            if (Entry(line, directory) is { } entry)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>
    /// One line: <c>mode links owner group size month day time name</c>, with
    /// <c>name -&gt; target</c> for a symbolic link. A name may hold spaces, so
    /// the split stops at the eighth field and keeps the rest whole.
    /// </summary>
    private static ContainerFileEntry? Entry(string line, string directory)
    {
        var text = line.TrimEnd('\r');
        if (text.Length == 0 || text.StartsWith("total ", StringComparison.Ordinal))
        {
            return null;
        }

        var parts = text.Split((char[]?)null, 9, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 9 || parts[0].Length == 0 || !"-dlbcsp".Contains(parts[0][0]))
        {
            return null;
        }

        var name = parts[8];
        var target = "";
        if (parts[0][0] == 'l' && name.Contains(" -> ", StringComparison.Ordinal))
        {
            var arrow = name.IndexOf(" -> ", StringComparison.Ordinal);
            target = name[(arrow + 4)..];
            name = name[..arrow];
        }

        if (name is "." or "..")
        {
            return null;
        }

        var size = long.TryParse(parts[4], out var bytes) ? bytes : 0;
        return new ContainerFileEntry(
            name,
            Type(parts[0][0]),
            parts[0],
            size,
            Bytes.Humanize(size),
            parts[2],
            parts[3],
            $"{parts[5]} {parts[6]} {parts[7]}",
            target,
            ContainerPath.Join(directory, name));
    }

    private static string Type(char mode) => mode switch
    {
        'd' => ContainerFileEntry.Directory,
        'l' => ContainerFileEntry.Link,
        '-' => ContainerFileEntry.File,
        _ => "other",
    };
}
