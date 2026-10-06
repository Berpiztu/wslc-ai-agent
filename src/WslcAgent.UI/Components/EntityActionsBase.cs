using Microsoft.AspNetCore.Components;
using MudBlazor;
using WslcAgent.ApiClient;

namespace WslcAgent.UI.Components;

/// <summary>
/// What every row-actions component shares: a busy flag while a verb runs
/// (also published to <see cref="BusyRows"/> so the row's dot and card show
/// it), reporting on the title bar's activity line, the removal confirmation, dialogs, and the
/// <see cref="Changed"/> callback the page refreshes on.
/// </summary>
public abstract class EntityActionsBase : ComponentBase
{
    [Inject] protected WslcAgentApi Api { get; set; } = default!;

    [Inject] protected ISnackbar Snackbar { get; set; } = default!;

    [Inject] protected IDialogService Dialogs { get; set; } = default!;

    [Inject] private BusyRows Rows { get; set; } = default!;

    [Inject] protected ActivityLine Activity { get; set; } = default!;

    [Inject] private OpenPopups Popups { get; set; } = default!;

    /// <summary>Raised after any verb or dialog that may have changed the row.</summary>
    [Parameter] public EventCallback Changed { get; set; }

    /// <summary>The row's identity, the same the page's <c>KeyOf</c> returns.</summary>
    protected abstract string Key { get; }

    protected bool Busy { get; private set; }

    /// <summary>A verb a later slice brings: it stays a normal button and says so.</summary>
    protected void NotYet(string feature, string slice) => Slices.NotYet(Snackbar, feature, slice);

    /// <summary>Bound to the row menu's <c>OpenChanged</c>: the page holds its refresh while the menu is open.</summary>
    protected void MenuOpenChanged(bool open) => Popups.Set(open);

    protected async Task RunAsync(string what, string verb, Func<CancellationToken, Task> action)
    {
        SetBusy(true);
        using var work = Activity.Begin($"{VerbWords.Gerund(verb)} {what}");
        try
        {
            await action(CancellationToken.None);
            work.Done($"{what}: {verb} ok", verb);
            await Changed.InvokeAsync();
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            work.Fail($"{what}: {verb} failed. {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Confirms, then removes.</summary>
    protected async Task RemoveAsync(string kind, string what, Func<CancellationToken, Task> remove)
    {
        if (await DialogFlow.ConfirmRemoveAsync(Dialogs, kind, what))
        {
            await RunAsync(what, "remove", remove);
        }
    }

    /// <summary>Opens a form dialog; the page refreshes when it was submitted.</summary>
    protected async Task OpenAsync<TDialog>(string title, DialogParameters? parameters = null)
        where TDialog : IComponent
    {
        if (await DialogFlow.ShowAsync<TDialog>(Dialogs, title, parameters))
        {
            await Changed.InvokeAsync();
        }
    }

    private void SetBusy(bool busy)
    {
        Busy = busy;
        if (busy)
        {
            Rows.Begin(Key);
        }
        else
        {
            Rows.End(Key);
        }
    }
}
