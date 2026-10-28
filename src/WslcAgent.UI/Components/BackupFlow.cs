using System.Text.RegularExpressions;
using MudBlazor;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components.Dialogs;

namespace WslcAgent.UI.Components;

/// <summary>
/// A backup and a restore, of a volume or of a container (<see cref="BackupKind"/>),
/// the same wherever they are asked for (a card or a row, a page's Backup
/// &amp; restore): each asks what it needs, starts the agent's job and returns;
/// the card's ring and the tables' bar follow the job, open its log and
/// cancel it. A backup goes to its backup folder (its own, else a folder
/// named after it in the folder of all of them), never asked at each backup.
/// A refusal is said in a window.
/// </summary>
public static partial class BackupFlow
{
    /// <summary>A volume's backups the host file picker lists: its last extension, <c>.gz</c> of <c>.tar.gz</c>.</summary>
    private const string BackupFiles = ".gz";

    /// <summary>One's Backup &amp; restore: it alone, its tabs, no list.</summary>
    public static Task ShowAsync(IDialogService dialogs, BackupKind kind, string name) =>
        DialogFlow.ShowAsync<BackupsDialog>(dialogs, $"Backup & restore — {name}",
            new DialogParameters<BackupsDialog> { { d => d.Kind, kind }, { d => d.Name, name } }, large: true, persistent: true);

    /// <summary>A page's Backup &amp; restore: every one at the left, All first, as wide as a project's window.</summary>
    public static Task ShowAllAsync(IDialogService dialogs, BackupKind kind) =>
        DialogFlow.ShowAsync<BackupsDialog>(dialogs, "Backup & restore",
            new DialogParameters<BackupsDialog> { { d => d.Kind, kind } }, wide: true, persistent: true);

    /// <summary>
    /// Starts the backup of <paramref name="name"/> into its backup folder, as
    /// its settings say (the agent's <see cref="BackupPreview"/>): what it
    /// holds, a container's asked, its parts ticked as its settings tick them;
    /// the running containers it needs stopped,
    /// stopped after asking, or without asking, and started again when it
    /// ends, or the backup refused while they run. False when it did not start.
    /// </summary>
    public static async Task<bool> BackupAsync(IDialogService dialogs, WslcAgentApi api, BackupKind kind, string name)
    {
        BackupPreview preview;
        try
        {
            preview = await api.GetBackupPreviewAsync(kind.Key, name);
            if (kind.InParts)
            {
                if (await DialogFlow.PickAsync<BackupWhatDialog>(dialogs, $"Back up {name}", new DialogParameters<BackupWhatDialog> { { d => d.What, preview.What } }) is not { } chosen)
                {
                    return false;
                }

                preview = await api.GetBackupPreviewAsync(kind.Key, name, chosen);
            }
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            await DialogFlow.ExplainAsync(dialogs, "Back up", "Back up did not start.", ex.Message);
            return false;
        }

        var stop = false;
        if (preview.Running is { Count: > 0 } running)
        {
            var one = running.Count == 1;
            var said = $"{string.Join(", ", running)} {(one ? "is" : "are")} running";
            if (preview.Stop == BackupChoice.No)
            {
                await DialogFlow.ExplainAsync(dialogs, "Back up", "Back up did not start.",
                    $"{said}, and {name}'s backup settings say not to stop {(one ? "it" : "them")}: stop {(one ? "it" : "them")} first, then back it up.");
                return false;
            }

            if (preview.Stop != BackupChoice.Yes
                && !await DialogFlow.ConfirmAsync(dialogs, $"Back up {name}",
                    $"{said}. Stop {(one ? "it" : "them")} to back up {name}? {(one ? "It is" : "They are")} started again when the backup ends.", "Stop and back up"))
            {
                return false;
            }

            stop = true;
        }

        return await StartAsync(dialogs, "Back up", () => kind.Backup(api, name, preview.What, stop));
    }

    /// <summary>
    /// A restore from a backup of the host: the backup (a volume's file, a
    /// container's folder), then what it goes into, <paramref name="name"/> to
    /// begin with, or the one the backup is named after (<c>hermes-data</c> of
    /// <c>hermes-data-20261027-101500.tar.gz</c>). One that is there has what
    /// the backup holds replace its own, said in red; one that is not is
    /// created: a new machine's. The picker opens on <paramref name="start"/>:
    /// its last backup, or its backup folder.
    /// </summary>
    public static async Task<bool> RestoreAsync(IDialogService dialogs, WslcAgentApi api, BackupKind kind, string name = "", string start = "")
    {
        var backup = kind.InFolders ? await Pickers.HostFolderAsync(dialogs, start) : await Pickers.HostFileAsync(dialogs, BackupFiles, start);
        if (backup is null
            || await DialogFlow.PromptAsync(dialogs, "Restore", kind.Title, $"Example: hermes-data. Created when it is not there; one that is has what the backup holds replace its own.", "Next",
                name.Length > 0 ? name : NameOf(backup)) is not { } into)
        {
            return false;
        }

        return await RestoreIntoAsync(dialogs, api, kind, into, backup);
    }

    /// <summary>A backup put back into <paramref name="name"/>, after saying what it does to it.</summary>
    public static async Task<bool> RestoreIntoAsync(IDialogService dialogs, WslcAgentApi api, BackupKind kind, string name, string backup) =>
        await ConfirmRestoreAsync(dialogs, api, kind, name, backup)
        && await StartAsync(dialogs, "Restore", () => kind.Restore(api, backup, name));

    /// <summary>A backup of the history deleted, its file or its folder with it, after asking.</summary>
    public static async Task<bool> DeleteAsync(IDialogService dialogs, WslcAgentApi api, BackupKind kind, BackupEntry backup)
    {
        if (!await DialogFlow.ConfirmAsync(dialogs, "Delete backup", $"Delete {backup.Path}? It goes from the disk, and it can no longer be restored.", "Delete", destructive: true))
        {
            return false;
        }

        try
        {
            await api.DeleteBackupAsync(kind.Key, backup.Path);
            return true;
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            await DialogFlow.ExplainAsync(dialogs, "Delete backup", "The backup was not deleted.", ex.Message);
            return false;
        }
    }

    /// <summary>Where backups go and how they are made, saved; a refusal (a folder that is not a full path) is said in a window.</summary>
    public static async Task<bool> SaveSettingsAsync(IDialogService dialogs, WslcAgentApi api, BackupKind kind, BackupSettings settings)
    {
        try
        {
            await api.SaveBackupSettingsAsync(kind.Key, settings);
            return true;
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            await DialogFlow.ExplainAsync(dialogs, "Backup settings", "The backup settings were not saved.", ex.Message);
            return false;
        }
    }

    /// <summary>The job's log, followed while it runs.</summary>
    public static Task ShowLogAsync(IDialogService dialogs, BackupKind kind, BackupJob job) =>
        DialogFlow.ShowAsync<BackupLogDialog>(dialogs, $"{Said(job.Kind)} log — {job.Name}",
            new DialogParameters<BackupLogDialog> { { d => d.Kind, kind }, { d => d.Job, job.Id } }, large: true);

    /// <summary>Asks, then stops a job under way; true when it was stopped.</summary>
    public static async Task<bool> CancelAsync(IDialogService dialogs, WslcAgentApi api, ISnackbar snackbar, BackupKind kind, BackupJob job)
    {
        var said = Said(job.Kind).ToLowerInvariant();
        var what = job.Kind == BackupJob.Backup ? "What was written of it is deleted." : $"{job.Name} is left as far as it got.";
        if (!await DialogFlow.ConfirmAsync(dialogs, $"Cancel {said}", $"Cancel the {said} of {job.Name}? {what}", $"Cancel {said}", destructive: true))
        {
            return false;
        }

        try
        {
            await api.CancelBackupJobAsync(kind.Key, job.Id);
            return true;
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            snackbar.Add($"The {said} was not cancelled: {ex.Message}", Severity.Error);
            return false;
        }
    }

    /// <summary>The cross of a job that ended: forgotten, for every client.</summary>
    public static async Task DismissAsync(WslcAgentApi api, ISnackbar snackbar, BackupKind kind, BackupJob job)
    {
        try
        {
            await api.DismissBackupJobAsync(kind.Key, job.Id);
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            snackbar.Add(ex.Message, Severity.Error);
        }
    }

    /// <summary>The job of one to show: the one under way, else the last that ended and is still listed.</summary>
    public static BackupJob? For(IEnumerable<BackupJob> jobs, string name) =>
        jobs.Where(job => job.Name == name).OrderByDescending(job => job.Running).ThenByDescending(job => job.StartedAt).FirstOrDefault();

    /// <summary><c>Backup</c> or <c>Restore</c>, as titles say it.</summary>
    public static string Said(string kind) => kind == BackupJob.Backup ? "Backup" : "Restore";

    /// <summary>What a backup is named after: its file's or its folder's name without the date the agent adds and a volume's extension.</summary>
    internal static string NameOf(string backup)
    {
        var name = Path.GetFileName(backup.Replace('\\', '/').TrimEnd('/'));
        name = name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? name[..^".tar.gz".Length] : name;
        return Dated().Replace(name, "");
    }

    /// <summary>One that is there has what the backup holds replace its own, said in red; one that is not is created, said plainly.</summary>
    private static async Task<bool> ConfirmRestoreAsync(IDialogService dialogs, WslcAgentApi api, BackupKind kind, string name, string backup) =>
        (await kind.Names(api)).Contains(name)
            ? await DialogFlow.ConfirmAsync(dialogs, "Restore", $"Restore {backup} into {name}? It must be stopped, and every container that uses what it puts back.", "Restore",
                danger: kind.Replaces(name))
            : await DialogFlow.ConfirmAsync(dialogs, "Restore", $"Restore {backup} into {name}? There is no {kind.Noun} {name}: it is created.", "Restore");

    /// <summary>Starts the job; a refusal (one busy, a folder that is not one) is said in a window.</summary>
    private static async Task<bool> StartAsync(IDialogService dialogs, string title, Func<Task<BackupJob>> start)
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
