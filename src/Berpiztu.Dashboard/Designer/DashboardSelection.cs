using Berpiztu.Dashboard.Model;

namespace Berpiztu.Dashboard.Designer;

/// <summary>
/// What is selected while the dashboard is shown: an object standing alone, or a
/// card — a group — whole, and the objects that are, so the page can offer
/// their verbs. Nothing selected is <see cref="None"/>.
/// </summary>
/// <param name="Object">The object standing alone selected; null for a card or nothing.</param>
/// <param name="Group">The card selected; null for an object or nothing.</param>
/// <param name="Objects">The objects selected: the one, or the card's.</param>
/// <param name="Opens">What is selected opens something fuller (a chart, a count's page).</param>
public sealed record DashboardSelection(ObjectInstance? Object, DashboardGroup? Group, IReadOnlyList<ObjectInstance> Objects, bool Opens)
{
    public static DashboardSelection None { get; } = new(null, null, [], false);
}
