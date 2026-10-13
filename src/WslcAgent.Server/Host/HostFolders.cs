using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Host;

/// <summary>
/// The agent machine's folders, for the host folder picker of a bind mount:
/// one level at a time, drives at the root, and "create a folder here". Asked
/// for kinds of files, the folder's files of those kinds with them: the same
/// picker choosing a file (a Compose file).
/// </summary>
public static class HostFolders
{
    private static readonly char[] KindSeparators = [',', ';'];

    /// <summary>
    /// Folders under <paramref name="path"/>; an empty path lists the drives.
    /// With <paramref name="files"/>, the extensions wanted (<c>.yaml,.yml</c>),
    /// the folder's files that have one of them too.
    /// </summary>
    public static HostFolderListing List(string path, string files = "")
    {
        var kinds = files.Split(KindSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(kind => kind.StartsWith('.') ? kind : "." + kind)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var text = path.Trim();
        if (text.Length == 0)
        {
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName).ToList();
            return new HostFolderListing("", "", drives, kinds.Count > 0 ? Array.Empty<string>() : null);
        }

        var full = Path.GetFullPath(text);
        if (!Directory.Exists(full))
        {
            throw new ArgumentException($"Not a folder: {text}", nameof(path));
        }

        var folders = Named(Directory.EnumerateDirectories(full));
        var parent = Path.GetDirectoryName(full) ?? "";
        return new HostFolderListing(full, parent, folders,
            kinds.Count > 0 ? Named(Directory.EnumerateFiles(full).Where(file => kinds.Contains(Path.GetExtension(file)))) : null);
    }

    /// <summary>The names of a folder's entries, in order, without the hidden and the system's own.</summary>
    private static List<string> Named(IEnumerable<string> entries) =>
        entries
            .Where(entry => (File.GetAttributes(entry) & (FileAttributes.Hidden | FileAttributes.System)) == 0)
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>A new folder under an existing one; the new path is returned.</summary>
    public static string Create(string parent, string name)
    {
        var folder = name.Trim();
        if (folder.Length == 0 || folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || folder is "." or "..")
        {
            throw new ArgumentException($"Invalid folder name: {name}", nameof(name));
        }

        var parentPath = Path.GetFullPath(parent.Trim());
        if (!Directory.Exists(parentPath))
        {
            throw new ArgumentException($"Not a folder: {parent}", nameof(parent));
        }

        var full = Path.Combine(parentPath, folder);
        Directory.CreateDirectory(full);
        return full;
    }
}
