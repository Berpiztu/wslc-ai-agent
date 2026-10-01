using System.Text.Json.Serialization;
using MudBlazor;

namespace WslcAgent.UI.Components.Styles;

/// <summary>
/// The look of the controls the application is built from, as numbers a person
/// can change: the ones the Layout tab of Settings edits. Every value here is
/// either a CSS variable the stylesheet already reads
/// (<see cref="UiStyle.Variables"/>) or a parameter one shared component takes,
/// so changing it changes every screen at once and never one screen alone.
/// <para>
/// The defaults are what the application ships with (docs/RULES.md): a value
/// that equals its default writes nothing, so the stylesheet keeps its own.
/// </para>
/// </summary>
public sealed class UiStyleValues
{
    /// <summary>Table rows (every list page at once, through ListGrid).</summary>
    public bool GridDense { get; set; } = true;

    public bool GridStriped { get; set; } = true;

    public bool GridBordered { get; set; } = true;

    public bool GridHover { get; set; } = true;

    public bool GridOutlined { get; set; } = true;

    /// <summary>The header row stays while the body scrolls; the lists ship with it.</summary>
    public bool GridFixedHeader { get; set; } = true;

    /// <summary>The shadow a list stands on; the application ships flat but for one step.</summary>
    public int GridElevation { get; set; } = 1;

    /// <summary>The header row of every list: its background (Default keeps the chrome tone), its words, and its shadow.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Color GridHeadColor { get; set; } = Color.Default;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Color GridHeadTextColor { get; set; } = Color.Default;

    public int GridHeadElevation { get; set; } = 2;

    /// <summary>What a cell keeps to its left and to its right, in pixels (MudBlazor's own are 16 and 24).</summary>
    public int RowPadStart { get; set; } = 7;

    public int RowPadEnd { get; set; } = 10;

    /// <summary>The glyphs of a row's actions, in pixels, and the room around each of them.</summary>
    public int ActionIconSize { get; set; } = 18;

    public int ActionPadding { get; set; } = 1;

    /// <summary>A bar's buttons: their height in pixels and their corners.</summary>
    public int ButtonHeight { get; set; } = 26;

    public int ButtonRadius { get; set; }

    /// <summary>The words on a bar's buttons, in pixels: what Size.Small and Size.Medium really differ in.</summary>
    public int ButtonFontSize { get; set; } = 13;

    /// <summary>What a bar's button keeps to the left and the right of its word, in pixels.</summary>
    public int ButtonPadding { get; set; } = 8;

    /// <summary>A bar's fields (the search box, the pickers), in pixels.</summary>
    public int FieldHeight { get; set; } = 26;

    /// <summary>The room a form's field keeps above its text, where the label sits: MudBlazor's dense margin is 23.</summary>
    public int FormPadTop { get; set; } = 23;

    /// <summary>And under it: MudBlazor's dense margin is 6.</summary>
    public int FormPadBottom { get; set; } = 6;

    /// <summary>The corners of a form's fields and selects, in pixels (the theme's radius).</summary>
    public int FormFieldRadius { get; set; } = 4;

    /// <summary>The checkbox glyph of a row and of a header row, in pixels.</summary>
    public int CheckSize { get; set; } = 16;

    /// <summary>The glyphs of a bar — search, refresh, view toggle, the buttons' own — in pixels.</summary>
    public int BarIconSize { get; set; } = 18;

    /// <summary>What stands between two controls of the same bar, in pixels.</summary>
    public int BarGap { get; set; } = 8;

    /// <summary>The tabs of a details screen, in pixels.</summary>
    public int TabHeight { get; set; } = 26;

    /// <summary>
    /// And the rest of what a set of tabs takes: they are the details screens'
    /// own. Rounded rounds the block's corners and Border draws its outline; the
    /// application ships with both.
    /// </summary>
    public bool TabsRounded { get; set; } = true;

    public bool TabsBorder { get; set; } = true;

    public bool TabsCentered { get; set; }

    public int TabsElevation { get; set; } = 2;

    /// <summary>The tab bar's background: Default keeps the chrome tone of the table headers.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Color TabsBarColor { get; set; } = Color.Default;

    /// <summary>The words of the tabs not chosen: Default keeps the secondary text colour; the chosen one stays green.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Color TabsTextColor { get; set; } = Color.Default;

    /// <summary>The narrowest a tab may be, in pixels: 0 lets a tab be as wide as its word and no wider.</summary>
    public int TabsMinWidth { get; set; }

    /// <summary>The table-or-cards switch of every list page (ViewModeToggle).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Size ToggleSize { get; set; } = Size.Small;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Color ToggleColor { get; set; } = Color.Primary;

    public bool ToggleOutlined { get; set; } = true;

    public bool ToggleDelimiters { get; set; }

    public bool ToggleRounded { get; set; }

    public bool ToggleCheckMark { get; set; }

    /// <summary>The card every list page shows a row as in cards view (EntityCard).</summary>
    public int CardElevation { get; set; } = 1;

    public bool CardOutlined { get; set; }

    public bool CardSquare { get; set; }

    /// <summary>
    /// The four type roles the application uses and nothing else:
    /// titles, the default text, the
    /// secondary text and the overline. Each one takes a standard MudBlazor
    /// typo — its size, its weight, its line and its letter spacing — so the
    /// application never invents a size: it chooses one of the theme's.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Typo TitleTypo { get; set; } = Typo.h6;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Typo DefaultTypo { get; set; } = Typo.body1;

    /// <summary>What a table's rows are written in: MudBlazor's body2, which is what the lists take.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Typo TableTypo { get; set; } = Typo.body2;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Typo SecondaryTypo { get; set; } = Typo.caption;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Typo OverlineTypo { get; set; } = Typo.overline;

    [JsonIgnore]
    public static UiStyleValues Defaults { get; } = new();

    public UiStyleValues Copy() => (UiStyleValues)MemberwiseClone();

    public bool Matches(UiStyleValues other) =>
        GridDense == other.GridDense
        && GridStriped == other.GridStriped
        && GridBordered == other.GridBordered
        && GridHover == other.GridHover
        && GridOutlined == other.GridOutlined
        && GridFixedHeader == other.GridFixedHeader
        && GridElevation == other.GridElevation
        && GridHeadColor == other.GridHeadColor
        && GridHeadTextColor == other.GridHeadTextColor
        && GridHeadElevation == other.GridHeadElevation
        && RowPadStart == other.RowPadStart
        && RowPadEnd == other.RowPadEnd
        && ActionIconSize == other.ActionIconSize
        && ActionPadding == other.ActionPadding
        && ButtonHeight == other.ButtonHeight
        && ButtonRadius == other.ButtonRadius
        && ButtonFontSize == other.ButtonFontSize
        && ButtonPadding == other.ButtonPadding
        && FieldHeight == other.FieldHeight
        && FormPadTop == other.FormPadTop
        && FormPadBottom == other.FormPadBottom
        && FormFieldRadius == other.FormFieldRadius
        && CheckSize == other.CheckSize
        && BarIconSize == other.BarIconSize
        && BarGap == other.BarGap
        && TabHeight == other.TabHeight
        && TabsRounded == other.TabsRounded
        && TabsBorder == other.TabsBorder
        && TabsCentered == other.TabsCentered
        && TabsElevation == other.TabsElevation
        && TabsBarColor == other.TabsBarColor
        && TabsTextColor == other.TabsTextColor
        && TabsMinWidth == other.TabsMinWidth
        && ToggleSize == other.ToggleSize
        && ToggleColor == other.ToggleColor
        && ToggleOutlined == other.ToggleOutlined
        && ToggleDelimiters == other.ToggleDelimiters
        && ToggleRounded == other.ToggleRounded
        && ToggleCheckMark == other.ToggleCheckMark
        && CardElevation == other.CardElevation
        && CardOutlined == other.CardOutlined
        && CardSquare == other.CardSquare
        && TitleTypo == other.TitleTypo
        && DefaultTypo == other.DefaultTypo
        && TableTypo == other.TableTypo
        && SecondaryTypo == other.SecondaryTypo
        && OverlineTypo == other.OverlineTypo;
}
