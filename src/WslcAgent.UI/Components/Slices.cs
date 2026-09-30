using MudBlazor;

namespace WslcAgent.UI.Components;

/// <summary>Verbs a later slice brings: they stay normal controls and say so.</summary>
public static class Slices
{
    public static void NotYet(ISnackbar snackbar, string feature, string slice) =>
        snackbar.Add($"{feature} arrives with the {slice} slice.", Severity.Info);
}
