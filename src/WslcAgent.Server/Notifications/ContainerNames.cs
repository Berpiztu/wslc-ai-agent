using WslcAgent.Mcp;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Notifications;

/// <summary>
/// The name a notification gives a container an event names by its id. The
/// event's bracket holds the name, but cannot be read honestly (see
/// <see cref="WslcEventParsing"/>): the list has it.
/// </summary>
public sealed class ContainerNames(IContainerService containers, ILogger<ContainerNames> logger)
{
    /// <summary>The container's name; its short id when the list does not have it, or cannot be read.</summary>
    public async Task<string> OfAsync(string id)
    {
        try
        {
            var list = await containers.ListAsync(all: true);
            if (list.Containers.FirstOrDefault(c => c.Id.Length > 0 && (id.StartsWith(c.Id, StringComparison.Ordinal) || c.Id.StartsWith(id, StringComparison.Ordinal))) is { } found)
            {
                return found.Name;
            }
        }
        catch (Exception failed) when (failed is WslcException or WslcNotFoundException or TimeoutException or InvalidOperationException)
        {
            logger.LogDebug("notifications: the container's name could not be read: {Message}", failed.Message);
        }

        return id.Length > 12 ? id[..12] : id;
    }
}
