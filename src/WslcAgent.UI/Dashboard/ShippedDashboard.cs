using Berpiztu.Dashboard.Model;
using Berpiztu.Dashboard.Storage;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// The dashboard as the agent ships it for a user with none: every page and both its
/// views, each without the objects that read one of this machine's resources — a
/// container, an image, a volume, a network, a chart of a container — since
/// every machine it is installed on has its own, and one of these would read
/// nothing there. A card left with none of its objects goes with them, and
/// the status bar's alarms with their objects.
/// </summary>
internal static class ShippedDashboard
{
    public static string Of(string whole) => DashboardPages.Each(whole, Portable);

    /// <summary>The layout without what reads one of this machine's resources.</summary>
    private static DashboardLayout Portable(DashboardLayout layout)
    {
        IReadOnlyList<ObjectInstance> objects = [.. layout.Objects.Where(o => RegistrySource.Uid(o.Source) is null)];
        return layout with
        {
            Objects = objects,
            Groups = [.. layout.Groups.Where(card => objects.Any(o => o.Group == card.Id))],
            Status = [.. layout.Status.Where(status => objects.Any(o => o.Id == status.Object))],
        };
    }
}
