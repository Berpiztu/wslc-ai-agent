using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>The "View container usage" texts, one line per container.</summary>
public static class UsageReports
{
    /// <summary>
    /// The containers that stand on an image, as the agent found them by the
    /// image each is pinned to. One made with a name that now belongs to
    /// another image (a newer pull or a patch took it, and left this image with
    /// no name) is said so: it is why an image can count a container that no
    /// search by name finds.
    /// </summary>
    public static string Image(ImageSummary image, ImageUsers users)
    {
        if (users.Containers.Count == 0)
        {
            return image.Containers is > 0
                ? $"wslc counts {image.Containers} container(s) on this image, but none is pinned to it now: the count may be of a files helper of the agent's, or of a container removed a moment ago."
                : "No containers using this image.";
        }

        var lines = users.Containers.Select(c => $"{(c.Name.Length > 0 ? c.Name : c.Id)}  ({c.State})  created as {c.CreatedAs}").ToList();
        var moved = users.Containers.Where(c => !Names(c.CreatedAs, image)).ToList();
        if (moved.Count > 0)
        {
            lines.Add("");
            lines.Add($"{string.Join(", ", moved.Select(c => c.Name.Length > 0 ? c.Name : c.Id))}: made with a name that now belongs to another image. "
                + $"A newer pull or a patch took the name, and this image ({users.Id}) was left without it, with its container still on it. "
                + "Remove the container to free this image, or recreate it to move it to the image its name has now.");
        }

        return string.Join('\n', lines);
    }

    /// <summary>The name a container was made with is this image's own: its reference, or its repository alone when the tag is latest.</summary>
    private static bool Names(string createdAs, ImageSummary image) =>
        !image.IsDangling
        && (ImageReference.Normalize(createdAs) == image.Reference
            || (image.Tag == ImageReference.Latest && ImageReference.Normalize(createdAs) == image.Repository));
    public static string Volume(VolumeUsers users) =>
        users.Containers.Count == 0
            ? "No containers using this volume."
            : string.Join('\n', users.Containers.Select(c =>
                $"{(c.Name.Length > 0 ? c.Name : c.Id)}  ({c.State})  {c.Destination}  [{(c.ReadWrite ? "rw" : "ro")}]  {c.Image}"));
}
