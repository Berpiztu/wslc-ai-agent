using System.Text.Json.Nodes;
using Berpiztu.Dashboard.Model;

namespace Berpiztu.Dashboard.Storage;

/// <summary>
/// A dashboard's two views kept as one text (the landscape and the portrait
/// one, each laid out on its own): the landscape
/// layout at the top, and the portrait one under <c>portrait</c>, absent
/// until it is first laid out. A text with the landscape view alone is the
/// landscape's alone.
/// </summary>
public static class DashboardViews
{
    private const string PortraitKey = "portrait";

    /// <summary>The layout of one view as it is kept; null for a portrait view never laid out.</summary>
    public static string? Read(string? stored, string view) =>
        view == DashboardView.Portrait ? StoredText.Parse(stored)?[PortraitKey]?.ToJsonString() : stored;

    /// <summary>The whole text with one view's layout written in it, the other view's kept as it was.</summary>
    public static string Write(string? stored, string view, string layout)
    {
        var whole = StoredText.Parse(stored) ?? [];
        if (view == DashboardView.Portrait)
        {
            whole[PortraitKey] = JsonNode.Parse(layout);
            return whole.ToJsonString();
        }

        var landscape = JsonNode.Parse(layout) as JsonObject ?? [];
        if (whole[PortraitKey] is { } portrait)
        {
            landscape[PortraitKey] = portrait.DeepClone();
        }

        return landscape.ToJsonString();
    }

    /// <summary>
    /// A view's layout given to the other (the dashboard designed for one
    /// view is where the other starts from): what
    /// it shows, where it shows it — each object and card at its place — each
    /// object as its kind is born in the other view (<see cref="IObjectDefaults"/>):
    /// its look and cells, its place in its card; where none was designed, at
    /// Medium, in either view alike (a card on a phone as the list
    /// pages draw it on a phone). A card dropped as a card made
    /// already takes that card's size there, and every card holds its objects.
    /// </summary>
    /// <param name="columns">The columns the other view's canvas has.</param>
    public static DashboardLayout Given(DashboardLayout layout, string to, int columns, IObjectDefaults? defaults)
    {
        IReadOnlyList<ObjectInstance> objects = [.. layout.Objects.Select(o => Born(o, layout.FindGroup(o.Group), to, defaults))];
        IReadOnlyList<DashboardGroup> groups = [.. layout.Groups.Select(card =>
        {
            var reach = CellBox.Around(objects.Where(o => o.Group == card.Id).Select(o => o.Box).DefaultIfEmpty(card.Box));
            var designed = card.Template is { } template ? defaults?.For(to, template) : null;
            return card with
            {
                W = Math.Max(designed?.W ?? 0, reach.X + reach.W - card.X),
                H = Math.Max(designed?.H ?? 0, reach.Y + reach.H - card.Y),
            };
        })];
        return new DashboardLayout(columns, objects) { Groups = groups, Status = layout.Status };
    }

    /// <summary>An object of one view as it is born in another, where the first has it.</summary>
    private static ObjectInstance Born(ObjectInstance o, DashboardGroup? card, string view, IObjectDefaults? defaults)
    {
        if (defaults?.For(view, o.Type) is not { } designed)
        {
            return o with { Size = TypeSize.Medium };
        }

        var born = designed.On(o);
        return card is not null && designed is { X: { } x, Y: { } y } ? born with { X = card.X + x, Y = card.Y + y } : born;
    }
}
