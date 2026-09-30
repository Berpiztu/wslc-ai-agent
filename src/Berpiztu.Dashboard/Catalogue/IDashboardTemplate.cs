using Berpiztu.Dashboard.Model;
using Berpiztu.Dashboard.Storage;

namespace Berpiztu.Dashboard.Catalogue;

/// <summary>
/// A card made already: several objects laid
/// out together, which the toolbox offers beside the objects and drops as one
/// group, each object in its place. The application gives its templates
/// (registered as this interface); the SDK knows them only as their objects'
/// types and places. What the card reads is chosen once, on the group.
/// </summary>
public interface IDashboardTemplate
{
    /// <summary>Its id, unique among the toolbox's entries (<c>wslc.container-card</c>).</summary>
    string Type { get; }

    /// <summary>What the toolbox calls it.</summary>
    string Label { get; }

    /// <summary>The toolbox's glyph for it, an SVG path as MudBlazor's icons are.</summary>
    string Icon { get; }

    /// <summary>The toolbox's group it stands in.</summary>
    string Group { get; }

    /// <summary>
    /// The heading it stands under within its group (the user's cards and the
    /// system's apart); subgroups go in the
    /// order their first card is given, and empty stands under none.
    /// </summary>
    string Subgroup { get; }

    /// <summary>Its objects and where each stands in the card, from the card's top-left cell.</summary>
    IReadOnlyList<TemplateItem> Items { get; }
}

/// <summary>
/// One object of a template: its type, its cells within the card, its type
/// size, and where the card needs them, the size of its parts, its vertical
/// alignment and its margin down (a resource card laid out as the
/// list pages draw theirs, its dials' rings a step smaller than their text and
/// standing at the top of the body); what it reads is chosen once, on the card.
/// </summary>
public sealed record TemplateItem(string Type, int X, int Y, int W, int H, TypeSize Size = TypeSize.Medium,
    IReadOnlyDictionary<string, PartStyle>? Parts = null, VerticalAlign? Vertical = null, int? MarginY = null);

/// <summary>A card made already as it is born: its template's type, its objects, each where it stands from the card's top-left cell, and its frame's size.</summary>
/// <param name="Frame">The card's own default in the view, whose look — its colours, its elevation, its anchor — its frame takes; none, and it takes the page's.</param>
public sealed record BornCard(string Template, IReadOnlyList<ObjectInstance> Objects, int W, int H, ObjectDefault? Frame = null)
{
    /// <summary>Its frame, standing at a cell under an id, as designed.</summary>
    public DashboardGroup Framed(string id, int x, int y)
    {
        var group = new DashboardGroup(id, x, y, W, H, Template: Template);
        return Frame?.On(group) ?? group;
    }
}

/// <summary>A card made already as it is dropped.</summary>
public static class DashboardTemplates
{
    /// <summary>
    /// The card as it is born in a view: each of its objects as its kind's
    /// default there has it where one was designed — its look, its cells and
    /// its place in the card, since the cards are designed on the board of
    /// every object — with the template's place,
    /// cells and type size otherwise, in either view alike; and the frame at its
    /// designed size, never smaller than its objects take. The objects' ids
    /// are their types, each card holding one of a kind, until it is dropped.
    /// </summary>
    public static BornCard Born(this IDashboardTemplate template, IObjectDefaults? defaults, string view)
    {
        if (template is HalvedCardsTemplate halved)
        {
            return halved.Born(defaults, view);
        }

        IReadOnlyList<ObjectInstance> objects = [.. template.Items.Select(item =>
        {
            var born = new ObjectInstance(item.Type, item.Type, item.X, item.Y, item.W, item.H, Size: item.Size,
                Vertical: item.Vertical, Parts: item.Parts, MarginY: item.MarginY);
            return defaults?.For(view, item.Type) is { } designed
                ? designed.On(born) with { X = designed.X ?? item.X, Y = designed.Y ?? item.Y }
                : born;
        })];
        var reach = CellBox.Around(objects.Select(o => o.Box));
        var frame = defaults?.For(view, template.Type);
        return new BornCard(template.Type, objects, Math.Max(frame?.W ?? 0, reach.X + reach.W), Math.Max(frame?.H ?? 0, reach.Y + reach.H), frame);
    }
}
