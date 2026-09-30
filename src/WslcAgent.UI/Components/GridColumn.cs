namespace WslcAgent.UI.Components;

/// <summary>
/// Column classes for <see cref="ListGrid{TItem}"/>. A data column is as wide
/// as it was given, never as wide as what is in it: a value that comes and goes
/// (CPU, memory, disk, ports while a container starts or stops) must not move
/// the columns after it, least of all the actions at the end. The last data
/// column takes whatever width is over, so there is no empty strip before those
/// actions. Widths are classes, never inline styles: MudBlazor writes the
/// user's drag-to-resize inline, and an inline HeaderStyle would override it.
/// </summary>
public static class GridColumn
{
    /// <summary>A narrow, title-less status-dot column (26px, the sort arrow included).</summary>
    public const string Bullet = "wslc-col-bullet";

    /// <summary>The row-selection checkbox column (32px).</summary>
    public const string Select = "wslc-col-select";

    /// <summary>The +/− column that opens a row's detail under it (28px), title-less.</summary>
    public const string Toggle = "wslc-col-toggle";

    /// <summary>The actions column: tight buttons, a small margin at both ends.</summary>
    public const string Actions = "wslc-col-actions";

    /// <summary>
    /// A column of numbers: the title and every value are right-aligned, so the
    /// figures line up on their last digit and are read down the column. Text
    /// columns stay at the left. A value with a unit (2.84%, 899.5MiB / 30.98GiB,
    /// 3000→8080) counts as a number.
    /// </summary>
    public const string Numeric = "wslc-col-num";

    /// <summary>
    /// A fixed width in characters, sized for the widest value the column shows
    /// and no wider. The steps that exist are the ones listed in
    /// <c>wslc-agent-ui.css</c>; a new one is a line there. Goes on the header
    /// and on the cells, so both keep it.
    /// </summary>
    public static string Width(int characters) => $"wslc-col-w wslc-col-w-{characters}";

    /// <summary>A column of numbers of a fixed width: the width and the alignment together.</summary>
    public static string Number(int characters) => $"{Width(characters)} {Numeric}";

    /// <summary>
    /// The last data column, which takes the width left over so the table has no
    /// empty strip between it and the actions. Its declared width is its
    /// minimum, for when the table is already wider than the page.
    /// </summary>
    public static string Stretch(int characters) => $"{Width(characters)} wslc-col-stretch";

    /// <summary>The last data column when it holds numbers.</summary>
    public static string StretchNumber(int characters) => $"{Stretch(characters)} {Numeric}";
}
