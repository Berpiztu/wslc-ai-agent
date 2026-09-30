using MudBlazor;

namespace WslcAgent.UI.Components;

/// <summary>
/// How a toggle in an action rail says its state, the same in every rail: off,
/// it is green and stands proud of the rail like every other button there;
/// on, it turns blue (the primary) and is drawn pressed into the rail
/// (<c>wslc-rail-on</c>).
/// </summary>
public static class RailToggle
{
    public static Color Color(bool on) => on ? MudBlazor.Color.Primary : MudBlazor.Color.Success;

    public static string? Class(bool on) => on ? "wslc-rail-on" : null;
}
