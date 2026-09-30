using System.Net.Http;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using WslcAgent.ApiClient;

namespace WslcAgent.UI.Components;

/// <summary>
/// What a family's page verbs share (Run and Create, Pull, Prune…): the
/// verbs a list page offers in the round button, and the dashboard too when
/// one of that family's cards is selected, so
/// they are written once. A verb that may have changed the family raises
/// <see cref="Changed"/>, which the page or the dashboard reads the family on.
/// </summary>
public abstract class PageVerbsBase : ComponentBase
{
    [Inject] protected WslcAgentApi Api { get; set; } = default!;

    [Inject] protected ISnackbar Snackbar { get; set; } = default!;

    [Inject] protected IDialogService Dialogs { get; set; } = default!;

    /// <summary>Raised after a verb that may have changed the family.</summary>
    [Parameter] public EventCallback Changed { get; set; }

    /// <summary>A page-level verb (prune, create…): runs, reports, refreshes.</summary>
    protected async Task RunAsync(string what, string verb, Func<CancellationToken, Task> action)
    {
        try
        {
            await action(CancellationToken.None);
            Snackbar.Add($"{what}: {verb} ok", Severity.Success);
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            Snackbar.Add($"{what}: {verb} failed. {ex.Message}", Severity.Error);
        }

        await Changed.InvokeAsync();
    }

    /// <summary>A destructive page-level verb: asks first.</summary>
    protected async Task ConfirmAndRunAsync(string title, string message, string yes, string what, Func<CancellationToken, Task> action)
    {
        if (await DialogFlow.ConfirmAsync(Dialogs, title, message, yes, destructive: true))
        {
            await RunAsync(what, yes.ToLowerInvariant(), action);
        }
    }

    /// <summary>Opens a form dialog; refreshes when it was submitted.</summary>
    protected async Task OpenAsync<TDialog>(string title, DialogParameters? parameters = null, bool large = false)
        where TDialog : IComponent
    {
        if (await DialogFlow.ShowAsync<TDialog>(Dialogs, title, parameters, large))
        {
            await Changed.InvokeAsync();
        }
    }
}
