using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Host;

/// <summary>
/// The agent machine's folders, for the host folder picker of a bind mount:
/// one level at a time, drives at the root, and "create a folder here".
/// </summary>
public static class HostFolders
{
    /// <summary>Folders under <paramref name="path"/>; an empty path lists the drives.</summary>
    public static HostFolderListing List(string path)
    {
        var text = path.Trim();
        if (text.Length == 0)
        {
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName).ToList();
            return new HostFolderListing("", "", drives);
        }

        var full = Path.GetFullPath(text);
        if (!Directory.Exists(full))
        {
            throw new ArgumentException($"Not a folder: {text}", nameof(path));
        }

        var folders = Directory.EnumerateDirectories(full)
            .Where(d => (File.GetAttributes(d) & (FileAttributes.Hidden | FileAttributes.System)) == 0)
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var parent = Path.GetDirectoryName(full) ?? "";
        return new HostFolderListing(full, parent, folders);
    }

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
