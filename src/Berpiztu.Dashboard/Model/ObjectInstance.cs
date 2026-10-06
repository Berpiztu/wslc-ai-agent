using System.Text.Json.Serialization;

namespace Berpiztu.Dashboard.Model;

/// <summary>An object's type size: Medium is how it draws itself, Small a step down, Large a step up.</summary>
public enum TypeSize
{
    Small,
    Medium,
    Large,
}

/// <summary>Where an object's content stands across its cells.</summary>
public enum HorizontalAlign
{
    Left,
    Center,
    Right,
}

/// <summary>Where an object's content stands down its cells.</summary>
public enum VerticalAlign
{
    Top,
    Middle,
    Bottom,
}

/// <summary>
/// One object on the dashboard: which kind it is, what it reads, where it
/// stands and how it is drawn. Everything the properties window changes is
/// here, and nothing else, so what is stored is what the user set.
/// </summary>
/// <param name="Id">The object's own, given when it is dropped and never changed.</param>
/// <param name="Type">
/// Its kind, the id its descriptor declares (<c>wslc.container-cpu</c>):
/// internal and fixed, never chosen by the user.
/// </param>
/// <param name="X">Its first column on the canvas, from 0.</param>
/// <param name="Y">Its first row on the canvas, from 0.</param>
/// <param name="W">How many columns it spans, at least 1.</param>
/// <param name="H">How many rows it spans, at least 1.</param>
/// <param name="Source">
/// Which one of its type's family it reads (a container's registry uid),
/// the one thing the user chooses about what it shows; null until chosen,
/// and always null for a type with no family.
/// </param>
/// <param name="Size">Its type size.</param>
/// <param name="Horizontal">Where its content stands across its cells; null is the middle, as an object stands until it is told otherwise.</param>
/// <param name="Vertical">Where its content stands down its cells; null is the middle.</param>
/// <param name="Group">
/// The group it stands in (<see cref="DashboardGroup"/>), inside that group's
/// frame; null for an object standing alone. A group means nothing: it only keeps its objects together.
/// </param>
/// <param name="Background">
/// The colour its cells are filled with; null is its kind's default
/// (<see cref="Catalogue.DashboardObjectAttribute.Background"/>), and is not written.
/// <see cref="ThemeColor.Inherited"/> takes its group's.
/// </param>
/// <param name="Foreground">
/// The colour of its text; null is its kind's default
/// (<see cref="Catalogue.DashboardObjectAttribute.Foreground"/>). <see cref="ThemeColor.Inherited"/>
/// takes its group's.
/// </param>
/// <param name="MarginX">
/// Pixels between its left and right edges and what it shows, so what is
/// aligned left or right keeps clear of the edge; null is none, and is not written.
/// </param>
/// <param name="MarginY">Pixels between its top and bottom edges and what it shows; null is none.</param>
/// <param name="Text">
/// The words the user wrote on an object that takes them
/// (<see cref="Catalogue.DashboardObjectAttribute.Writes"/>: a free text);
/// null for one with none, or for any other object.
/// </param>
/// <param name="Alarms">
/// Its alarms as the user set them, one per measure its kind watches
/// (<see cref="Catalogue.DashboardObjectAlarmAttribute"/>); a measure not in
/// them has its kind's own. Null while none was touched.
/// </param>
/// <param name="Hidden">
/// A piece of a card hidden by the user (<see cref="Catalogue.DashboardObjectAttribute.CardOnly"/>):
/// not drawn, in design or out of it, and taking no cells; its card's
/// properties show it again. Null is shown.
/// </param>
/// <param name="Parts">
/// What the user changed of each of its parts, by the part's key
/// (<see cref="Catalogue.DashboardObjectPartAttribute"/>); null while every part
/// is the object's design.
/// </param>
/// <param name="Elevation">
/// How far it stands off what is under it, 0 to <see cref="DashboardGroup.MaxElevation"/>
/// as MudBlazor's shadows go (every object has one, as every card has); null is its design's: standing alone,
/// where it is drawn as a card, the application's for its cards; in a card, none.
/// </param>
/// <param name="Anchor">
/// Which of its card's edges it keeps to as the card widens with a fluid view
/// (<see cref="HorizontalAnchor"/>); null is <see cref="HorizontalAnchor.Scale"/>.
/// </param>
/// <param name="Border">
/// Standing alone, where it is drawn as a card: the colour of that card's
/// line, as a card's <see cref="DashboardGroup.Border"/>; Transparent is none,
/// null its default. In a card its card's line is the one drawn.
/// </param>
public sealed record ObjectInstance(
    string Id,
    string Type,
    int X,
    int Y,
    int W,
    int H,
    string? Source = null,
    TypeSize Size = TypeSize.Medium,
    HorizontalAlign? Horizontal = null,
    VerticalAlign? Vertical = null,
    string? Group = null,
    ThemeColor? Background = null,
    ThemeColor? Foreground = null,
    IReadOnlyDictionary<string, PartStyle>? Parts = null,
    int? MarginX = null,
    int? MarginY = null,
    bool? Hidden = null,
    string? Text = null,
    IReadOnlyList<ObjectAlarm>? Alarms = null,
    int? Elevation = null,
    HorizontalAnchor? Anchor = null,
    ThemeColor? Border = null)
{
    /// <summary>The cells it stands on; worked out, never stored.</summary>
    [JsonIgnore]
    public CellBox Box => new(X, Y, W, H);

    /// <summary>What the user changed of this part; nothing while it is the object's design.</summary>
    public PartStyle PartOf(string key) => Parts?.GetValueOrDefault(key) ?? new PartStyle();

    /// <summary>The object with this part's style changed; a part back to the object's design is not kept.</summary>
    public ObjectInstance WithPart(string key, PartStyle style)
    {
        var parts = (Parts ?? new Dictionary<string, PartStyle>())
            .Where(part => part.Key != key)
            .Concat(style.IsDesign ? [] : [KeyValuePair.Create(key, style)])
            .ToDictionary(part => part.Key, part => part.Value);
        return this with { Parts = parts.Count > 0 ? parts : null };
    }

    /// <summary>The object with this alarm set as the user set it, the others kept.</summary>
    public ObjectInstance WithAlarm(ObjectAlarm alarm) =>
        this with { Alarms = [.. (Alarms ?? []).Where(a => a.Measure != alarm.Measure), alarm] };

    /// <summary>The same object moved, its size kept.</summary>
    public ObjectInstance MovedBy(int dx, int dy) => this with { X = X + dx, Y = Y + dy };
}
