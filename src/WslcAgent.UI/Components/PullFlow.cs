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
        DialogFlow.ShowAsync<PullLogDialog>(dialogs, "Push progress", new DialogParameters<PullLogDialog> { { d => d.Image, image }, { d => d.Kind, ImageJobKind.Push } }, large: true);

    /// <summary>An image's own check: its window, that image alone. A card's or a row's look at a check under way, or ended, is this same window, which opens on it.</summary>
    public static Task<bool> ShowCheckAsync(IDialogService dialogs, string image) =>
        DialogFlow.ShowAsync<CheckImageDialog>(dialogs, $"Check image — {image}", new DialogParameters<CheckImageDialog> { { d => d.Reference, image } }, large: true);

    /// <summary>The page's Check: every image at the left, the same pane at the right, as wide as the backups' window.</summary>
    public static Task<bool> ShowChecksAsync(IDialogService dialogs) =>
        DialogFlow.ShowAsync<CheckImageDialog>(dialogs, "Check images", wide: true);

    /// <summary>The log of a patch's build job, in the view logs are read in (<see cref="BuildLogDialog"/>).</summary>
    public static Task<bool> ShowBuildLogAsync(IDialogService dialogs, string job, string image) =>
        DialogFlow.ShowAsync<BuildLogDialog>(dialogs, $"Patch log — {image}", new DialogParameters<BuildLogDialog> { { d => d.Job, job } }, large: true);

    /// <summary>Asks, then stops a patch's build; true when it was stopped. A container it was to move runs on as it was.</summary>
    public static async Task<bool> CancelBuildAsync(IDialogService dialogs, WslcAgentApi api, ISnackbar snackbar, string job)
    {
        if (!await DialogFlow.ConfirmAsync(dialogs, "Cancel patch", "Are you sure you want to cancel this patch? Its containers run on as they were.", "Cancel patch", destructive: true))
        {
            return false;
        }

        try
        {
            await api.CancelImageBuildAsync(job);
            return true;
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            snackbar.Add($"The patch was not cancelled: {ex.Message}", Severity.Error);
            return false;
        }
    }

    /// <summary>Asks, then stops the pull (or the push, or the check); true when it was running and is stopping.</summary>
    public static async Task<bool> CancelAsync(IDialogService dialogs, WslcAgentApi api, ISnackbar snackbar, string image, ImageJobKind kind = ImageJobKind.Pull)
    {
        var noun = kind.Verb().ToLowerInvariant();
        if (!await DialogFlow.ConfirmAsync(dialogs, $"Cancel {noun}", $"Are you sure you want to cancel this {noun}?", $"Cancel {noun}", destructive: true))
        {
            return false;
        }

        try
        {
            return (await api.CancelAsync(kind, image)).Cancelled;
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

    /// <summary>The cross of a check that ended: the agent forgets it and the card is the image's again.</summary>
    public static async Task DismissCheckAsync(WslcAgentApi api, ISnackbar snackbar, string image)
    {
        try
        {
            await api.DismissImageCheckAsync(image);
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
