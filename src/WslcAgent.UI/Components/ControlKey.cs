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

/// <summary>A key pressed on the row, with the modifiers that were held for it.</summary>
public readonly record struct ControlKeyPress(ControlKey Key, ControlModifiers Modifiers);

/// <summary>How each control key reads on its button and in its tooltip.</summary>
public static class ControlKeyNames
{
    /// <summary>The keys a terminal wants most: leaving, completing, the history and moving along the line.</summary>
    public static readonly IReadOnlyList<ControlKey> Cursor =
        [ControlKey.Escape, ControlKey.Tab, ControlKey.Up, ControlKey.Down, ControlKey.Left, ControlKey.Right, ControlKey.Home, ControlKey.End];

    public static string Label(ControlKey key) => key switch
    {
        ControlKey.Escape => "Esc",
        ControlKey.Tab => "Tab",
        ControlKey.Up => "↑",
        ControlKey.Down => "↓",
        ControlKey.Left => "←",
        ControlKey.Right => "→",
        ControlKey.Home => "Home",
        ControlKey.End => "End",
        _ => key.ToString(),
    };

    public static string Title(ControlKey key) => key switch
    {
        ControlKey.Up => "Up arrow",
        ControlKey.Down => "Down arrow",
        ControlKey.Left => "Left arrow",
        ControlKey.Right => "Right arrow",
        _ => key.ToString(),
    };
}
