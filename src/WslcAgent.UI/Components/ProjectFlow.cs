using MudBlazor;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components.Dialogs;

namespace WslcAgent.UI.Components;

/// <summary>What the Containers page and the project's windows share about a project: its window of two panes, the log of its save, and cancelling the save.</summary>
public static class ProjectFlow
{
    /// <summary>
    /// A project's window of two panes (<see cref="ProjectPanesDialog"/>): its containers at the left and
    /// <paramref name="tabs"/> at the right, one alone the pane itself (Logs, Update image, Zaindari), several
    /// in tabs (Project). Its size is the screen's, taken when it opens.
    /// </summary>
    public static Task<bool> ShowPanesAsync(IDialogService dialogs, string title, string project, IReadOnlyList<ContainerSummary> members, params ProjectTab[] tabs) =>
        DialogFlow.ShowAsync<ProjectPanesDialog>(dialogs, title,
            new DialogParameters<ProjectPanesDialog> { { d => d.Project, project }, { d => d.Members, members }, { d => d.Tabs, tabs } }, wide: true);

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
