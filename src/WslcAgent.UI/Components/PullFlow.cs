using MudBlazor;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components.Dialogs;

namespace WslcAgent.UI.Components;

/// <summary>What the Images and Containers pages share about a pull the agent runs — its console, cancelling it, forgetting it, matching it to a row — and the console of a push, which is the same one.</summary>
public static class PullFlow
{
    /// <summary>The pull's console; <paramref name="finished"/> when the row already knows the pull has ended, so the console paints at once instead of waiting for it to start.</summary>
    public static Task<bool> ShowLogAsync(IDialogService dialogs, string image, bool finished = false) =>
        DialogFlow.ShowAsync<PullLogDialog>(dialogs, "Pull progress", new DialogParameters<PullLogDialog> { { d => d.Image, image }, { d => d.Finished, finished } }, large: true);

    /// <summary>The console of a push the agent runs (a publish), the pull's own with the push's output.</summary>
    public static Task<bool> ShowPushLogAsync(IDialogService dialogs, string image) =>
        DialogFlow.ShowAsync<PullLogDialog>(dialogs, "Push progress", new DialogParameters<PullLogDialog> { { d => d.Image, image }, { d => d.Push, true } }, large: true);

    /// <summary>Asks, then stops the pull (or the push); true when it was running and is stopping.</summary>
    public static async Task<bool> CancelAsync(IDialogService dialogs, WslcAgentApi api, ISnackbar snackbar, string image, bool push = false)
    {
        var noun = push ? "push" : "pull";
        if (!await DialogFlow.ConfirmAsync(dialogs, $"Cancel {noun}", $"Are you sure you want to cancel this {noun}?", $"Cancel {noun}", destructive: true))
        {
            return false;
        }

        try
        {
            return (await (push ? api.CancelImagePushAsync(image) : api.CancelImagePullAsync(image))).Cancelled;
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            snackbar.Add(ex.Message, Severity.Error);
            return false;
        }
    }

    /// <summary>The cross of a failed pull: the agent forgets it and the row goes, for every client. Nothing to confirm, nothing is lost.</summary>
    public static async Task DismissAsync(WslcAgentApi api, ISnackbar snackbar, string image)
    {
        try
        {
            await api.DismissImagePullAsync(image);
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            snackbar.Add(ex.Message, Severity.Error);
        }
    }

    /// <summary>The pull a catalog row stands for: the same reference, or the same repository and tag (<c>latest</c> when none).</summary>
    public static ImagePullState? For(IEnumerable<ImagePullState> pulls, ImageSummary row) =>
        pulls.FirstOrDefault(pull =>
        {
            // A row without its tag is the image a pull moved the tag away from, never the one pulled.
            var (repository, tag) = ImageReference.Split(pull.Image);
            return row.Reference == pull.Image || (row.Repository == repository && row.Tag == tag);
        });

    /// <summary>A row for a pull whose image is not in the catalog yet.</summary>
    public static ImageSummary PendingRow(ImagePullState pull)
    {
        var (repository, tag) = ImageReference.Split(pull.Image);
        return new ImageSummary("pending", repository, tag, pull.Image, "", "", "", null, false);
    }
}
