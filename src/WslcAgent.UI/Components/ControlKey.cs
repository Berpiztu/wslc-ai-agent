namespace WslcAgent.UI.Components;

/// <summary>A key a phone's on-screen keyboard usually lacks, offered by <see cref="ControlKeys"/>.</summary>
public enum ControlKey
{
    Escape,
    Tab,
    Up,
    Down,
    Left,
    Right,
    Home,
    End,
    PageUp,
    PageDown,
    Slash,

    /// <summary>A modifier, held for the next key (<see cref="ControlModifiers"/>).</summary>
    Ctrl,

    /// <summary>A modifier, held for the next key.</summary>
    Alt,

    /// <summary>A modifier, locked until pressed again.</summary>
    Shift,
}

/// <summary>Ctrl, Alt and Shift as <see cref="ControlKeys"/> holds them: Ctrl and Alt for the next key, Shift until it is pressed again.</summary>
[Flags]
public enum ControlModifiers
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
}

/// <summary>A key pressed, with the modifiers that were held for it.</summary>
public readonly record struct ControlKeyPress(ControlKey Key, ControlModifiers Modifiers);

/// <summary>The control keys' layouts, and how each key reads on its button and in its tooltip.</summary>
public static class ControlKeyNames
{
    /// <summary>
    /// A terminal's keys as Termux lays them out: two rows of seven, the arrows
    /// as a cross with Home and End beside the up arrow and the pages at the
    /// right; Shift where Termux has the minus, which the phone's keyboard has
    /// at hand, so Shift and the arrows select in nano.
    /// </summary>
    public static readonly IReadOnlyList<IReadOnlyList<ControlKey>> Terminal =
    [
        [ControlKey.Escape, ControlKey.Slash, ControlKey.Shift, ControlKey.Home, ControlKey.Up, ControlKey.End, ControlKey.PageUp],
        [ControlKey.Tab, ControlKey.Ctrl, ControlKey.Alt, ControlKey.Left, ControlKey.Down, ControlKey.Right, ControlKey.PageDown],
    ];

    /// <summary>The modifier a key holds; none for a key that is sent.</summary>
    public static ControlModifiers ModifierOf(ControlKey key) => key switch
    {
        ControlKey.Ctrl => ControlModifiers.Ctrl,
        ControlKey.Alt => ControlModifiers.Alt,
        ControlKey.Shift => ControlModifiers.Shift,
        _ => ControlModifiers.None,
    };

    public static string Label(ControlKey key) => key switch
    {
        ControlKey.Escape => "Esc",
        ControlKey.Up => "↑",
        ControlKey.Down => "↓",
        ControlKey.Left => "←",
        ControlKey.Right => "→",
        ControlKey.PageUp => "PgUp",
        ControlKey.PageDown => "PgDn",
        ControlKey.Slash => "/",
        _ => key.ToString(),
    };

    public static string Title(ControlKey key) => key switch
    {
        ControlKey.Up => "Up arrow",
        ControlKey.Down => "Down arrow",
        ControlKey.Left => "Left arrow",
        ControlKey.Right => "Right arrow",
        ControlKey.PageUp => "Page up",
        ControlKey.PageDown => "Page down",
        ControlKey.Ctrl => "Ctrl: held for the next key",
        ControlKey.Alt => "Alt (Meta): held for the next key",
        ControlKey.Shift => "Shift: locked until pressed again",
        _ => key.ToString(),
    };
}
