using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using WslcAgent.ApiClient;

namespace WslcAgent.UI.Components;

/// <summary>
/// The snackbar every screen injects, with two rules added. An error stays on
/// screen until the user closes it, and carries a Copy button for its text: a
/// failure the user did not read is a failure they will meet again, so only the
/// good news fades on its own; and a failure worth reporting is worth copying
/// whole instead of retyping from a toast. And while the agent does not answer
/// (<see cref="AgentLink"/>) no error is shown at all: every one of them is that
/// same "Failed to fetch", and the layout already says it, once, over the
/// screen. Both rules live here instead of at the thirty-odd places that
/// report an error.
/// </summary>
public sealed class ErrorsStaySnackbar(SnackbarService snackbars, IJSRuntime js, AgentLink link) : ISnackbar
{
    public IEnumerable<Snackbar> ShownSnackbars => snackbars.ShownSnackbars;

    public SnackbarConfiguration Configuration => snackbars.Configuration;

    public event Action? OnSnackbarsUpdated
    {
        add => snackbars.OnSnackbarsUpdated += value;
        remove => snackbars.OnSnackbarsUpdated -= value;
    }

    public Snackbar? Add(string message, Severity severity = Severity.Normal, Action<SnackbarOptions>? configure = null, string? key = null) =>
        Silenced(severity) ? null : snackbars.Add(message, severity, Stay(severity, configure, message), key);

    public Snackbar? Add(MarkupString message, Severity severity = Severity.Normal, Action<SnackbarOptions>? configure = null, string? key = null) =>
        Silenced(severity) ? null : snackbars.Add(message, severity, Stay(severity, configure, message.Value), key);

    /// <summary>A rendered message has no text of its own to copy; it still stays.</summary>
    public Snackbar? Add(RenderFragment message, Severity severity = Severity.Normal, Action<SnackbarOptions>? configure = null, string? key = null) =>
        Silenced(severity) ? null : snackbars.Add(message, severity, Stay(severity, configure, text: null), key);

    public Snackbar? Add<T>(Dictionary<string, object>? componentParameters = null, Severity severity = Severity.Normal, Action<SnackbarOptions>? configure = null, string? key = null)
        where T : IComponent =>
        Silenced(severity) ? null : snackbars.Add<T>(componentParameters, severity, Stay(severity, configure, text: null), key);

    public void Clear() => snackbars.Clear();

    public void Remove(Snackbar snackbar) => snackbars.Remove(snackbar);

    public void RemoveByKey(string key) => snackbars.RemoveByKey(key);

    public void Dispose() => snackbars.Dispose();

    /// <summary>An error while the agent does not answer is the link, already said by the layout.</summary>
    private bool Silenced(Severity severity) => severity == Severity.Error && !link.Online;

    /// <summary>
    /// The caller's own options first, then the rule: an error waits for a click
    /// and shows the cross to close it with, and — unless the caller gave the
    /// toast an action of its own — a Copy that puts the text on the clipboard.
    /// MudBlazor closes a toast on any action, so Copy also closes it.
    /// </summary>
    private Action<SnackbarOptions> Stay(Severity severity, Action<SnackbarOptions>? configure, string? text) =>
        options =>
        {
            configure?.Invoke(options);
            if (severity != Severity.Error)
            {
                return;
            }

            options.RequireInteraction = true;
            options.ShowCloseIcon = true;
            if (text is { Length: > 0 } && options.Action is null)
            {
                options.Action = "Copy";
                options.ActionColor = Color.Inherit;
                options.OnClick = _ => js.InvokeVoidAsync("navigator.clipboard.writeText", text).AsTask();
            }
        };
}
