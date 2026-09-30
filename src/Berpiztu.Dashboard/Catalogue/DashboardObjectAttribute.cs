using Berpiztu.Dashboard.Model;

namespace Berpiztu.Dashboard.Catalogue;

/// <summary>
/// An object's descriptor, written on its own component: this is all it
/// takes for the catalogue to find it and the toolbox to offer it, so adding
/// an object is adding a folder with its component, and nothing central
/// changes.
/// </summary>
/// <param name="type">
/// Its type id, written in the user's stored dashboard and never changed:
/// prefixed by whose it is, so two applications never collide
/// (<c>wslc.container-cpu</c>; the SDK's own <c>berpiztu.text</c>).
/// </param>
/// <param name="label">What the toolbox and the properties window call it.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DashboardObjectAttribute(string type, string label) : Attribute
{
    public string Type { get; } = type;

    public string Label { get; } = label;

    /// <summary>
    /// The toolbox's group it stands in, under a heading of this name and a
    /// line above it; groups go by name, and
    /// the objects in each by theirs. Empty stands in a group with no heading.
    /// </summary>
    public string Group { get; init; } = "";

    /// <summary>
    /// The branch of the toolbox's Objects its group stands in, as the cards
    /// stand in theirs under Cards (User, System): each a heading with its line, indented under Objects, its
    /// groups indented under it. Empty stands straight under Objects.
    /// </summary>
    public string Subgroup { get; init; } = "";

    /// <summary>The toolbox's glyph for it, an SVG path as MudBlazor's icons are.</summary>
    public string Icon { get; init; } = "";

    /// <summary>
    /// The family its source comes from (a <see cref="Sources.ISourceFamily.Key"/>,
    /// <c>container</c>), or empty for an object that reads nothing chosen,
    /// which has no source at all.
    /// </summary>
    public string Source { get; init; } = "";

    /// <summary>
    /// The source it reads until another is chosen (a chart's <c>host</c>), so
    /// it draws at once instead of waiting for one; empty for an object that
    /// needs its source chosen first. The field shows it, and it is not written.
    /// </summary>
    public string DefaultSource { get; init; } = "";

    /// <summary>The columns it spans when it is dropped.</summary>
    public int Columns { get; init; } = 4;

    /// <summary>The rows it spans when it is dropped.</summary>
    public int Rows { get; init; } = 4;

    /// <summary>
    /// It takes all its cells, as a card's header or its row of verbs covers
    /// its width with its tone, instead of standing at its size where its
    /// alignment puts it: the object places what it shows inside by its
    /// alignment itself.
    /// </summary>
    public bool Fills { get; init; }

    /// <summary>
    /// The ground it has until another is chosen, shown by name in its
    /// Background: a card's header or its row of verbs is born
    /// in the tone it has on the card, not painted there behind the property.
    /// </summary>
    public ThemeColor Background { get; init; } = ThemeColors.ObjectBackground;

    /// <summary>The colour of its text until another is chosen, shown by name in its Foreground.</summary>
    public ThemeColor Foreground { get; init; } = ThemeColors.ObjectForeground;

    /// <summary>
    /// Where what it shows stands across its cells until another alignment is
    /// chosen, marked as chosen in the properties window (with none marked,
    /// nobody knew where it stood). A
    /// card's header, its details and its shortcuts stand at the left, its
    /// verbs at the right.
    /// </summary>
    public HorizontalAlign Horizontal { get; init; } = HorizontalAlign.Center;

    /// <summary>Where what it shows stands down its cells until another alignment is chosen: a card's details at the top.</summary>
    public VerticalAlign Vertical { get; init; } = VerticalAlign.Middle;

    /// <summary>
    /// Which of its card's edges it keeps to as a fluid view widens the card,
    /// until another anchor is chosen: a resource card's header, details and verbs to both, its dials to the
    /// right, its shortcuts to the left, as the list pages' card behaves; every
    /// other kind spreads in proportion.
    /// </summary>
    public HorizontalAnchor Anchor { get; init; } = HorizontalAnchor.Scale;

    /// <summary>
    /// A piece of a card made already, which lives only in it (a chart card's
    /// summary, legend and chart are moved and changed inside it, never taken
    /// out): the toolbox does not offer it, it never leaves its card, the card
    /// takes no other object and is never ungrouped, and removing it hides it
    /// (<see cref="Model.ObjectInstance.Hidden"/>), so it can be shown again.
    /// </summary>
    public bool CardOnly { get; init; }

    /// <summary>The user writes its words (a free text): the properties window offers its Text, kept on the object.</summary>
    public bool Writes { get; init; }
}
