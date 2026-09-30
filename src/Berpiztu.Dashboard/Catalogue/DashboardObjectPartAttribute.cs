namespace Berpiztu.Dashboard.Catalogue;

/// <summary>
/// One part of what an object draws — a dial's ring, its figure, its name; a
/// reading's label, value and hint — written on the object's component beside
/// its descriptor, one per part, in the order the properties window shows
/// them. Each part takes a size of its own on top of the object's, and a
/// part of text a colour and a weight too.
/// What a part looks like until then is the object's design, and stays so
/// until the user changes it.
/// </summary>
/// <param name="key">The part's name in the stored dashboard and in the stylesheet (<c>figure</c>): lower-case letters, never changed.</param>
/// <param name="label">What the properties window calls it.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DashboardObjectPartAttribute(string key, string label) : Attribute
{
    public string Key { get; } = key;

    public string Label { get; } = label;

    /// <summary>A part of text, which takes a colour and a weight as well as a size; a ring or a line takes a size only.</summary>
    public bool Text { get; init; } = true;

    /// <summary>The part is bold in the object's design until the user says otherwise.</summary>
    public bool Bold { get; init; }

    /// <summary>A part drawn as a line — a dial's ring — which takes a thickness too, in the sizes' proportion.</summary>
    public bool Line { get; init; }

    /// <summary>
    /// The user can hide the part, and the object draws itself without it (a
    /// chart is its summary, its legend and its lines, each shown or not, none
    /// an object of its own).
    /// </summary>
    public bool Optional { get; init; }
}
