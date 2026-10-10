using MudBlazor;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components.Dialogs;

namespace WslcAgent.UI.Components;

/// <summary>What the Containers page and the project's log window share about a project being saved from its Compose file: opening that window, and cancelling the save.</summary>
public static class ProjectFlow
{
    /// <summary>The log of the save a row stands for, in the window a container's logs open in: every step the agent takes, and what the builds print.</summary>
    public static Task<bool> ShowLogAsync(IDialogService dialogs, ContainerLaunch row) =>
        DialogFlow.ShowAsync<ProjectLogDialog>(dialogs, $"Logs — {row.Name}", new DialogParameters<ProjectLogDialog> { { d => d.Job, row.Project } }, large: true);

    /// <summary>Asks, then stops the save where it is; true when the agent took the cancel. Nothing is removed: what it has made stays.</summary>
    public static async Task<bool> CancelAsync(IDialogService dialogs, WslcAgentApi api, ISnackbar snackbar, string job)
    {
        if (!await DialogFlow.ConfirmAsync(dialogs, "Cancel project", "Cancel saving this project? What it has already made stays.", "Cancel project", destructive: true))
        {
            return false;
        }

        try
        {
            await api.CancelContainerLaunchAsync(job);
            return true;
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            snackbar.Add(ex.Message, Severity.Error);
            return false;
        }
    }
}
