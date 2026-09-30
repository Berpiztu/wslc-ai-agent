using MudBlazor;

namespace WslcAgent.UI.Components;

/// <summary>
/// The toast a verb raises when it worked, in the colour of the button that ran
/// it: green for Start, the application's blue for Stop, so the message and the
/// control that caused it read as one thing. MudBlazor paints a toast from its
/// severity and its severities have no blue of ours, so that one arrives through
/// the class MudBlazor would otherwise set itself.
/// </summary>
public static class VerbToast
{
    private const string Blue = "wslc-toast-primary";

    /// <summary>Verbs whose button is the application's blue.</summary>
    private static readonly string[] BlueVerbs = ["stop"];

    /// <summary>Reports a verb that worked.</summary>
    public static void Done(ISnackbar snackbar, string message, string verb) =>
        snackbar.Add(message, Severity.Success, options =>
        {
            if (BlueVerbs.Contains(verb, StringComparer.OrdinalIgnoreCase))
            {
                options.SnackbarTypeClass = Blue;
            }
        });
}
