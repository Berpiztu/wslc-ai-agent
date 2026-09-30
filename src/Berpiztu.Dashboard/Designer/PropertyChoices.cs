using Berpiztu.Dashboard.Fields;
using Berpiztu.Dashboard.Model;

namespace Berpiztu.Dashboard.Designer;

/// <summary>
/// What the properties offer and how a choice is kept, shared by the fields
/// of one object and those of several chosen together.
/// </summary>
internal static class PropertyChoices
{
    /// <summary>The type sizes the Size field offers, in their order.</summary>
    public static readonly IReadOnlyList<FieldOption<TypeSize>> SizeOptions =
    [
        new(TypeSize.Small, "Small"),
        new(TypeSize.Medium, "Medium"),
        new(TypeSize.Large, "Large"),
    ];

    /// <summary>The colours Background and Foreground offer: Inherited, then the theme's by name.</summary>
    public static readonly IReadOnlyList<FieldOption<ThemeColor>> ColorOptions =
        [.. Enum.GetValues<ThemeColor>().Select(color => new FieldOption<ThemeColor>(color, ThemeColors.Label(color)))];

    /// <summary>The sizes for several objects at once: none of them marked while theirs differ.</summary>
    public static readonly IReadOnlyList<FieldOption<TypeSize?>> SomeSizeOptions =
        [.. SizeOptions.Select(size => new FieldOption<TypeSize?>(size.Value, size.Text))];

    /// <summary>The colours for several objects at once: none of them marked while theirs differ.</summary>
    public static readonly IReadOnlyList<FieldOption<ThemeColor?>> SomeColorOptions =
        [.. ColorOptions.Select(color => new FieldOption<ThemeColor?>(color.Value, color.Text))];

    /// <summary>A card's elevations (as the Containers screen's cards have one): the application's own for its cards, then 0 to the highest.</summary>
    public static readonly IReadOnlyList<FieldOption<int?>> ElevationOptions =
    [
        new(null, "Settings"),
        .. Enumerable.Range(0, DashboardGroup.MaxElevation + 1).Select(elevation => new FieldOption<int?>(elevation, $"{elevation}")),
    ];

    /// <summary>The anchors an object keeps to as a fluid view widens its card, for one object or several (none marked while theirs differ).</summary>
    public static readonly IReadOnlyList<FieldOption<HorizontalAnchor?>> AnchorOptions =
    [
        new(HorizontalAnchor.Scale, "Scale"),
        new(HorizontalAnchor.Left, "Left"),
        new(HorizontalAnchor.Right, "Right"),
        new(HorizontalAnchor.Both, "Both"),
    ];

    /// <summary>An alarm's thresholds, in steps of five percent.</summary>
    public static readonly IReadOnlyList<FieldOption<int>> ThresholdOptions =
        [.. Enumerable.Range(1, 20).Select(step => new FieldOption<int>(step * 5, $"{step * 5}%"))];

    /// <summary>A colour, an alignment, a size or a weight chosen as it is kept: none when it is the default, so a later default is followed.</summary>
    public static T? Kept<T>(T chosen, T byDefault) where T : struct =>
        EqualityComparer<T>.Default.Equals(chosen, byDefault) ? null : chosen;

    /// <summary>What several things have alike; null while they differ, or when there are none.</summary>
    public static T? Alike<T>(IEnumerable<T> values) where T : struct =>
        values.Distinct().ToList() is [var one] ? one : null;
}
