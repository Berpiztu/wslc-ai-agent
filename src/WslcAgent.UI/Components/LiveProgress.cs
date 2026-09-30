namespace WslcAgent.UI.Components;

/// <summary>
/// A line a toast keeps repainting while work goes on — "Stopping 2 of 5",
/// "Downloading update… 37%" — kept apart from the toast that shows it.
/// MudBlazor's snackbar holds the message it was created with, so a toast that
/// has to change is a toast whose content is a component listening here: it
/// repaints in place instead of one toast piling up per step.
/// </summary>
public sealed class LiveProgress
{
    public string Text { get; private set; } = "";

    public event Action? Changed;

    public void Set(string text)
    {
        if (Text == text)
        {
            return;
        }

        Text = text;
        Changed?.Invoke();
    }
}
