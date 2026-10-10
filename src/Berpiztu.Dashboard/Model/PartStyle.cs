namespace Berpiztu.Dashboard.Model;

/// <summary>
/// What the user changed of one part of an object: its size on top of the object's, its colour, whether it is bold, and
/// a line's thickness, whether a part the object lets the user hide is
/// hidden, and a box's background and alignment. Each is null while the part is as the object designs it, and is
/// not written; a style with nothing changed is not kept at all.
/// </summary>
/// <param name="Size">The part's size, a step down or up from the object's: Medium is the object's own.</param>
/// <param name="Color">The part's colour; null, or <see cref="ThemeColor.Inherited"/>, is the object's design.</param>
/// <param name="Bold">Whether the part is bold; null is the object's design.</param>
/// <param name="Thickness">A line's thickness, a step down or up from its design's; null is the design's.</param>
/// <param name="Hidden">The part is not drawn (a chart's summary, legend or lines, each shown or not); null is shown.</param>
/// <param name="Background">A box's ground (<see cref="Catalogue.DashboardObjectPartAttribute.Box"/>); null, or <see cref="ThemeColor.Inherited"/>, is the object's design.</param>
/// <param name="Horizontal">A box's alignment across; null is the object's design.</param>
/// <param name="Vertical">A box's alignment down; null is the object's design.</param>
public sealed record PartStyle(TypeSize? Size = null, ThemeColor? Color = null, bool? Bold = null, TypeSize? Thickness = null, bool? Hidden = null,
    ThemeColor? Background = null, HorizontalAlign? Horizontal = null, VerticalAlign? Vertical = null)
{
    /// <summary>Nothing of the part changed: it is the object's design.</summary>
    public bool IsDesign => Size is null && Color is null && Bold is null && Thickness is null && Hidden is null
        && Background is null && Horizontal is null && Vertical is null;
}

/// <summary>What each type size is, in one proportion, whatever it sizes.</summary>
public static class TypeSizes
{
    /// <summary>
    /// A size as a multiple of Medium: Small three quarters, Large a third
    /// more, so each size is about a third larger than the one below: Small to
    /// Medium and Medium to Large are one proportion.
    /// </summary>
    public static double Scale(TypeSize size) => size switch
    {
        TypeSize.Small => 0.75,
        TypeSize.Large => 1.3333,
        _ => 1,
    };
}
