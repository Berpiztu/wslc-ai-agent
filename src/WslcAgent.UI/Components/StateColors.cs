using MudBlazor;

namespace WslcAgent.UI.Components;

/// <summary>One mapping from a state word to a colour, used by the chip and the dot.</summary>
public static class StateColors
{
    public static Color For(string state) => state switch
    {
        "running" or "in use" => Color.Success,
        "paused" => Color.Warning,
        "restarting" or ApiClient.Contracts.ContainerSummary.Recreating => Color.Info,
        "created" => Color.Info,
        "dead" => Color.Error,
        _ => Color.Default,
    };
}
