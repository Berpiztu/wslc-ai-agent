namespace Berpiztu.Dashboard.Model;

/// <summary>
/// A colour an object or a group is given: one of the theme's, by name, so it follows the theme light or dark, or
/// <see cref="Inherited"/>, which takes the colour of what it stands in.
/// </summary>
public enum ThemeColor
{
    /// <summary>What it stands in: an object's group, or the page for an object standing alone or a group.</summary>
    Inherited,

    /// <summary>No colour of its own: what is under it shows.</summary>
    Transparent,

    /// <summary>The page's ground.</summary>
    Background,

    /// <summary>A card's ground.</summary>
    Surface,

    /// <summary>
    /// The theme's third colour, the application's magenta: under Surface in
    /// the lists, where it is found at once.
    /// Kept by name, so its place here changes no dashboard.
    /// </summary>
    Tertiary,

    /// <summary>The palette's grey ground: the tone a reading of the System card stands on.</summary>
    BackgroundGray,

    /// <summary>A card's ground washed with Primary: the tone a list's card gives its header and its verbs.</summary>
    PrimaryTint,

    /// <summary>The theme's text.</summary>
    Text,

    /// <summary>The theme's quieter text.</summary>
    TextSecondary,

    Primary,
    Secondary,
    Info,
    Success,
    Warning,
    Error,
    Dark,
}

/// <summary>
/// What each colour is on the page, and the colour each thing has until it is
/// given another: the one it has always had, so nothing changes until the
/// user changes it.
/// </summary>
public static class ThemeColors
{
    /// <summary>
    /// An object's cells: a card's ground, as a card stands on the page
    /// (Transparent let the canvas through, and an object was the canvas's
    /// colour).
    /// </summary>
    public const ThemeColor ObjectBackground = ThemeColor.Surface;

    public const ThemeColor ObjectForeground = ThemeColor.Text;

    /// <summary>A group's card, as it always stood: a card's ground.</summary>
    public const ThemeColor GroupBackground = ThemeColor.Surface;

    public const ThemeColor GroupForeground = ThemeColor.Text;

    /// <summary>
    /// How much Primary <see cref="ThemeColor.PrimaryTint"/> takes over a
    /// card's ground: the share WSLC's list cards give their header and verbs
    /// rows, made of the palette, so it follows
    /// the theme light or dark.
    /// </summary>
    private const int PrimaryTintShare = 18;

    /// <summary>How a colour reads in a list.</summary>
    public static string Label(ThemeColor color) => color switch
    {
        ThemeColor.TextSecondary => "Text secondary",
        ThemeColor.PrimaryTint => "Primary tint",
        ThemeColor.BackgroundGray => "Background gray",
        _ => color.ToString(),
    };

    /// <summary>
    /// A colour as CSS, the theme's palette variable by name; null for
    /// <see cref="ThemeColor.Inherited"/>, which is worked out from what the
    /// thing stands in.
    /// </summary>
    public static string? Css(ThemeColor color) => color switch
    {
        ThemeColor.Inherited => null,
        ThemeColor.Transparent => "transparent",
        ThemeColor.Text => "var(--mud-palette-text-primary)",
        ThemeColor.TextSecondary => "var(--mud-palette-text-secondary)",
        ThemeColor.BackgroundGray => "var(--mud-palette-background-gray)",
        ThemeColor.PrimaryTint => $"color-mix(in srgb, var(--mud-palette-primary) {PrimaryTintShare}%, var(--mud-palette-surface))",
        _ => $"var(--mud-palette-{color.ToString().ToLowerInvariant()})",
    };
}
