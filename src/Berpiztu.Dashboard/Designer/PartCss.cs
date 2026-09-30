using System.Globalization;
using Berpiztu.Dashboard.Model;

namespace Berpiztu.Dashboard.Designer;

/// <summary>
/// An object's parts as the canvas hands them to what the object draws: for
/// each part the user changed, its size as a multiple of the object's
/// (<c>--bz-part-{key}-scale</c>), a line's thickness as a multiple of its
/// design's (<c>--bz-part-{key}-thickness</c>), its colour (<c>--bz-part-{key}-color</c>)
/// and its weight (<c>--bz-part-{key}-weight</c>), with a class saying which
/// of the last two are set (<c>bz-part-{key}-color</c>,
/// <c>bz-part-{key}-weight</c>), so the application's stylesheet overrides
/// its design only there. A part as designed hands nothing.
/// </summary>
internal static class PartCss
{
    /// <summary>The custom properties of the parts changed, for the object's style.</summary>
    public static string Style(ObjectInstance o) =>
        string.Concat((o.Parts ?? new Dictionary<string, PartStyle>()).SelectMany(part => Properties(part.Key, part.Value)));

    /// <summary>The classes of the parts whose colour or weight changed, for the object's class.</summary>
    public static string Classes(ObjectInstance o) =>
        string.Concat((o.Parts ?? new Dictionary<string, PartStyle>()).SelectMany(part => ClassesOf(part.Key, part.Value)));

    private static IEnumerable<string> Properties(string key, PartStyle style)
    {
        if (style.Size is { } size)
        {
            yield return $"; --bz-part-{key}-scale: {TypeSizes.Scale(size).ToString(CultureInfo.InvariantCulture)}";
        }

        if (style.Color is { } color && ThemeColors.Css(color) is { } css)
        {
            yield return $"; --bz-part-{key}-color: {css}";
        }

        if (style.Thickness is { } thickness)
        {
            yield return $"; --bz-part-{key}-thickness: {TypeSizes.Scale(thickness).ToString(CultureInfo.InvariantCulture)}";
        }

        if (style.Bold is { } bold)
        {
            yield return $"; --bz-part-{key}-weight: {(bold ? 700 : 400)}";
        }
    }

    private static IEnumerable<string> ClassesOf(string key, PartStyle style)
    {
        if (style.Color is { } color && ThemeColors.Css(color) is not null)
        {
            yield return $" bz-part-{key}-color";
        }

        if (style.Bold is not null)
        {
            yield return $" bz-part-{key}-weight";
        }
    }
}
