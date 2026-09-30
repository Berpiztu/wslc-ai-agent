using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>The "View container usage" texts, one line per container.</summary>
public static class UsageReports
{
    /// <summary>
    /// wslc counts the containers of each image but never says which image id a
    /// container is pinned to: a container row carries only the image name, and
    /// two images can share a repository (a pull moves the tag and leaves the old
    /// image untagged). So the count is the authority and names only fill in the
    /// list, with a note when the two disagree.
    /// </summary>
    public static string Image(ImageSummary image, IReadOnlyList<ContainerSummary> containers)
    {
        if (image.Containers == 0)
        {
            return "No containers using this image.";
        }

        var repository = ImageReference.RepositoryOf(image.Reference);
        var users = containers
            .Where(c => c.Image == repository || c.Image.StartsWith(repository + ":", StringComparison.Ordinal))
            .ToList();
        if (users.Count == 0)
        {
            return image.Containers is int reported
                ? $"wslc reports {reported} container(s) on this image, but none matched by name."
                : "No containers using this image.";
        }

        var lines = users.Select(c => $"{(c.Name.Length > 0 ? c.Name : c.Id)}  ({c.State})  {c.Image}").ToList();
        if (image.Containers is int count && count != users.Count)
        {
            lines.Add("");
            lines.Add($"Note: wslc reports {count} container(s) on this image; {users.Count} share the repository name. wslc does not expose which image id a container is pinned to.");
        }

        return string.Join('\n', lines);
    }

    public static string Volume(VolumeUsers users) =>
        users.Containers.Count == 0
            ? "No containers using this volume."
            : string.Join('\n', users.Containers.Select(c =>
                $"{(c.Name.Length > 0 ? c.Name : c.Id)}  ({c.State})  {c.Destination}  [{(c.ReadWrite ? "rw" : "ro")}]  {c.Image}"));
}
