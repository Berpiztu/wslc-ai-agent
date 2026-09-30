using System.Text.Json;
using MudBlazor;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>The windows an image and a volume both open from their rows: their files, their View &amp; edit, and a report such as container usage.</summary>
public static class ResourceViews
{
    /// <summary>Files through a temporary helper container, removed when the window closes.</summary>
    public static Task FilesAsync(IDialogService dialogs, string title, string label, Func<Task<FilesSession>> open, Func<FilesSession, Task> close) =>
        DialogFlow.ShowAsync<Dialogs.HelperFilesDialog>(dialogs, title, new DialogParameters<Dialogs.HelperFilesDialog>
        {
            { d => d.Label, label },
            { d => d.Open, open },
            { d => d.Close, close },
        }, large: true);

    public static Task InspectAsync(
        IDialogService dialogs, string title, Func<Task<string>> load, Func<JsonElement, IReadOnlyList<InspectFact>> facts, Func<JsonElement, IReadOnlyList<InspectFact>> fields) =>
        DialogFlow.ShowAsync<Dialogs.InspectFormDialog>(dialogs, title, new DialogParameters<Dialogs.InspectFormDialog>
        {
            { d => d.Load, load },
            { d => d.Facts, facts },
            { d => d.Fields, fields },
        }, large: true);

    /// <summary>Plain text in the logs box, loaded when the window opens.</summary>
    public static Task ReportAsync(IDialogService dialogs, string title, Func<Task<string>> load) =>
        DialogFlow.ShowAsync<Dialogs.JsonDialog>(dialogs, title, new DialogParameters<Dialogs.JsonDialog> { { d => d.Load, load } }, large: true);
}
