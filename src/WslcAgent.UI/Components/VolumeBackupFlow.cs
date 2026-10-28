using System.Text.RegularExpressions;
using MudBlazor;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components.Dialogs;

namespace WslcAgent.UI.Components;

/// <summary>
/// A volume's backup and restore, the same wherever they are asked for (a
/// volume's card or row, the Volumes page's Backup &amp; restore): each asks
/// what it needs, starts the agent's job and returns; the card's ring and the
/// window's title follow the job, open its log and cancel it. A backup goes to
/// the volume's backup folder (its own, else every volume's, else the agent's
/// default), never asked at each backup. A refusal is said in a window.
/// </summary>
public static partial class VolumeBackupFlow
{
    /// <summary>The backups the host file picker lists: its last extension, <c>.gz</c> of <c>.tar.gz</c>.</summary>
    private const string BackupFiles = ".gz";

    /// <summary>A volume's Backup &amp; restore: that volume alone, its tabs, no list.</summary>
    public static Task ShowAsync(IDialogService dialogs, string volume) =>
        DialogFlow.ShowAsync<VolumeBackupsDialog>(dialogs, $"Backup & restore — {volume}",
            new DialogParameters<VolumeBackupsDialog> { { d => d.Volume, volume } }, large: true, persistent: true);

    /// <summary>The Volumes page's Backup &amp; restore: every volume at the left, All first, as wide as a project's window.</summary>
    public static Task ShowAllAsync(IDialogService dialogs) =>
        DialogFlow.ShowAsync<VolumeBackupsDialog>(dialogs, "Backup & restore", wide: true, persistent: true);

    /// <summary>Starts the backup of <paramref name="volume"/> into its backup folder; false when it did not start.</summary>
    public static Task<bool> BackupAsync(IDialogService dialogs, WslcAgentApi api, string volume) =>
        StartAsync(dialogs, "Back up", () => api.BackupVolumeAsync(volume));

    /// <summary>
    /// A restore from a backup file of the host: the file, then the volume it
    /// goes into, <paramref name="volume"/> to begin with, or the one the file
    /// is named after (<c>hermes-data</c> of <c>hermes-data-20261027-101500.tar.gz</c>).
    /// A volume that is there has its contents replaced, said in red; one that
    /// is not is created: a new machine's. The picker opens on
    /// <paramref name="start"/>: the volume's last backup, or its backup folder.
    /// </summary>
    public static async Task<bool> RestoreAsync(IDialogService dialogs, WslcAgentApi api, string volume = "", string start = "")
    {
        if (await Pickers.HostFileAsync(dialogs, BackupFiles, start) is not { } file
            || await DialogFlow.PromptAsync(dialogs, "Restore", "Volume", "Example: hermes-data. Created when it is not there; one that is has its contents replaced.", "Next",
                volume.Length > 0 ? volume : VolumeOf(file)) is not { } into)
        {
            return false;
        }

        return await RestoreIntoAsync(dialogs, api, into, file);
    }

    /// <summary>A backup put back into <paramref name="volume"/>, after saying what it does to it.</summary>
    public static async Task<bool> RestoreIntoAsync(IDialogService dialogs, WslcAgentApi api, string volume, string file) =>
        await ConfirmRestoreAsync(dialogs, api, volume, file)
        && await StartAsync(dialogs, "Restore", () => api.RestoreVolumeAsync(file, volume));

    /// <summary>A backup of the history deleted, its file with it, after asking.</summary>
    public static async Task<bool> DeleteAsync(IDialogService dialogs, WslcAgentApi api, VolumeBackup backup)
    {
        if (!await DialogFlow.ConfirmAsync(dialogs, "Delete backup", $"Delete {backup.Path}? The file goes, and it can no longer be restored.", "Delete", destructive: true))
        {
            return false;
        }

        try
        {
            await api.DeleteVolumeBackupAsync(backup.Path);
            return true;
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            await DialogFlow.ExplainAsync(dialogs, "Delete backup", "The backup was not deleted.", ex.Message);
            return false;
        }
    }

    /// <summary>Where backups go, saved; a refusal (a folder that is not a full path) is said in a window.</summary>
    public static async Task<bool> SaveSettingsAsync(IDialogService dialogs, WslcAgentApi api, VolumeBackupSettings settings)
    {
        try
        {
            await api.SaveVolumeBackupSettingsAsync(settings);
            return true;
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            await DialogFlow.ExplainAsync(dialogs, "Backup folder", "The backup folders were not saved.", ex.Message);
            return false;
        }
    }

    /// <summary>The job's log, followed while it runs.</summary>
    public static Task ShowLogAsync(IDialogService dialogs, VolumeBackupJob job) =>
        DialogFlow.ShowAsync<VolumeBackupLogDialog>(dialogs, $"{Said(job.Kind)} log — {job.Volume}",
            new DialogParameters<VolumeBackupLogDialog> { { d => d.Job, job.Id } }, large: true);

    /// <summary>Asks, then stops a job under way; true when it was stopped.</summary>
    public static async Task<bool> CancelAsync(IDialogService dialogs, WslcAgentApi api, ISnackbar snackbar, VolumeBackupJob job)
    {
        var said = Said(job.Kind).ToLowerInvariant();
        var what = job.Kind == VolumeBackupJob.Backup ? "The half-written file is deleted." : $"{job.Volume} is left as far as it got.";
        if (!await DialogFlow.ConfirmAsync(dialogs, $"Cancel {said}", $"Cancel the {said} of {job.Volume}? {what}", $"Cancel {said}", destructive: true))
        {
            return false;
        }

        try
        {
            await api.CancelVolumeBackupJobAsync(job.Id);
            return true;
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            snackbar.Add($"The {said} was not cancelled: {ex.Message}", Severity.Error);
            return false;
        }
    }

    /// <summary>The cross of a job that ended: forgotten, for every client.</summary>
    public static async Task DismissAsync(WslcAgentApi api, ISnackbar snackbar, VolumeBackupJob job)
    {
        try
        {
            await api.DismissVolumeBackupJobAsync(job.Id);
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            snackbar.Add(ex.Message, Severity.Error);
        }
    }

    /// <summary>The job of a volume to show: the one under way, else the last that ended and is still listed.</summary>
    public static VolumeBackupJob? For(IEnumerable<VolumeBackupJob> jobs, string volume) =>
        jobs.Where(job => job.Volume == volume).OrderByDescending(job => job.Running).ThenByDescending(job => job.StartedAt).FirstOrDefault();

    /// <summary><c>Backup</c> or <c>Restore</c>, as titles say it.</summary>
    public static string Said(string kind) => kind == VolumeBackupJob.Backup ? "Backup" : "Restore";

    /// <summary>The volume a backup's file is named after: its name without the date the agent adds and the extension.</summary>
    internal static string VolumeOf(string file)
    {
        var name = Path.GetFileName(file.Replace('\\', '/'));
        name = name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? name[..^".tar.gz".Length] : Path.GetFileNameWithoutExtension(name);
        return Dated().Replace(name, "");
    }

    /// <summary>A volume that is there has its contents replaced, said in red; one that is not is created, said plainly.</summary>
    private static async Task<bool> ConfirmRestoreAsync(IDialogService dialogs, WslcAgentApi api, string volume, string file) =>
        (await api.GetVolumesAsync()).Volumes.Any(v => v.Name == volume)
            ? await DialogFlow.ConfirmAsync(dialogs, "Restore", $"Restore {file} into {volume}? Containers that use it must be stopped.", "Restore",
                danger: $"Everything {volume} holds now is replaced by the backup's contents.")
            : await DialogFlow.ConfirmAsync(dialogs, "Restore", $"Restore {file} into {volume}? There is no volume {volume}: it is created.", "Restore");

    /// <summary>Starts the job; a refusal (the volume busy, a folder that is not one) is said in a window.</summary>
    private static async Task<bool> StartAsync(IDialogService dialogs, string title, Func<Task<VolumeBackupJob>> start)
    {
        try
        {
            await start();
            return true;
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            await DialogFlow.ExplainAsync(dialogs, title, $"{title} did not start.", ex.Message);
            return false;
        }
    }

    [GeneratedRegex(@"-\d{8}-\d{6}$")]
    private static partial Regex Dated();
}
