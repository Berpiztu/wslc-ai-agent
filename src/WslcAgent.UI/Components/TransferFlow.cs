using MudBlazor;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components.Dialogs;

namespace WslcAgent.UI.Components;

/// <summary>
/// What every screen showing files on the move shares: which transfers belong
/// to a container, the one ring that stands for several of them, and stopping
/// or forgetting them. The Containers list, the Files view and the dashboard's
/// card all read the same agent list through this, so a transfer says the same
/// thing wherever it is looked at.
/// </summary>
public static class TransferFlow
{
    /// <summary>How many names a tooltip lists before it counts the rest instead.</summary>
    private const int Named = 6;

    /// <summary>
    /// The transfers of one container. The agent keeps the container as the
    /// client that started the transfer named it — its id or its name — so a
    /// row answers to both.
    /// </summary>
    public static IReadOnlyList<ContainerTransfer> For(IEnumerable<ContainerTransfer> transfers, string id, string name) =>
        transfers.Where(transfer => transfer.Container == id || transfer.Container == name).ToList();

    /// <summary>The ones still moving, which are what a ring is about.</summary>
    public static IReadOnlyList<ContainerTransfer> Active(IEnumerable<ContainerTransfer> transfers) =>
        transfers.Where(transfer => transfer.Active).ToList();

    /// <summary>
    /// The ones a row shows: what is still moving, what is waiting its turn in
    /// the agent's queue, and what ended badly and has not been dismissed. One
    /// that went through is off the row the moment it is through — the file
    /// being there is the better news, and a ring that turns green for two
    /// seconds only asks what went wrong.
    /// </summary>
    public static IReadOnlyList<ContainerTransfer> Showing(IEnumerable<ContainerTransfer> transfers) =>
        transfers.Where(transfer => transfer.IsShowing).ToList();

    /// <summary>
    /// The percentage of several as one: each transfer weighed by how big it
    /// is, so a gigabyte at a tenth and a small file already through do not
    /// read as half way. A transfer whose size nobody knows counts as one byte,
    /// which leaves it out of the weighing without leaving it out of the ring.
    /// </summary>
    public static int Pct(IReadOnlyList<ContainerTransfer> transfers)
    {
        // Only what is moving: one that failed sits at nothing and would drag
        // the others' ring backwards.
        var moving = Active(transfers);
        if (moving.Count == 0)
        {
            return 0;
        }

        var total = moving.Sum(transfer => Math.Max(transfer.TotalBytes, 1));
        var done = moving.Sum(transfer => transfer.Pct * Math.Max(transfer.TotalBytes, 1));
        return (int)Math.Clamp(done / total, 0, 100);
    }

    /// <summary>
    /// What the ring says when the pointer rests on it: the one transfer's own
    /// step and where the file is going, or a line for each of several — the
    /// arrow says which way — and the reason when one of them failed.
    /// <para>
    /// The ones waiting their turn in the agent's queue are counted at the end
    /// rather than named: only one file travels at a time, so a row where
    /// sixteen were announced would otherwise say it has one and the other
    /// fifteen would look lost.
    /// </para>
    /// </summary>
    public static string Status(IReadOnlyList<ContainerTransfer> transfers)
    {
        var travelling = transfers.Where(transfer => !transfer.IsWaiting).ToList();
        var waiting = transfers.Count - travelling.Count;
        var moving = travelling switch
        {
            [] => "",
            [var one] => one.Error.Length > 0 ? $"{one.Error} ({one.Path})" : $"{one.Status} → {one.Path}",
            _ => string.Join("\n", travelling.Take(Named).Select(Line))
                + (travelling.Count > Named ? $"\n… and {travelling.Count - Named} more" : ""),
        };

        if (waiting == 0)
        {
            return moving;
        }

        var queued = waiting == 1 ? "1 file waiting its turn" : $"{waiting} files waiting their turn";
        return moving.Length == 0 ? queued : $"{moving}\n{queued}";
    }

    /// <summary>Whether the row's ring is the error one: nothing is moving or waiting any more and what is left ended badly.</summary>
    public static bool Failed(IReadOnlyList<ContainerTransfer> transfers) =>
        transfers.Count > 0 && transfers.All(transfer => !transfer.Active && !transfer.IsWaiting);

    /// <summary>
    /// The container's own list of files on the move, which the ring's
    /// magnifier opens: every one of them with its bar and its cross, and the
    /// ones still waiting their turn in this client under them.
    /// </summary>
    public static Task ShowAsync(IDialogService dialogs, string container, string name) =>
        DialogFlow.ShowAsync<TransfersDialog>(dialogs, "Transfers", new DialogParameters<TransfersDialog>
        {
            { d => d.Container, container },
            { d => d.Name, name.Length > 0 ? name : container },
        });

    /// <summary>
    /// Stops what is still moving, after asking once however many there are:
    /// bytes already sent are thrown away, so this is never silent.
    /// </summary>
    public static async Task<bool> CancelAsync(IDialogService dialogs, WslcAgentApi api, ISnackbar snackbar, FileTransfers mine, IReadOnlyList<ContainerTransfer> transfers)
    {
        var active = Active(transfers);
        if (active.Count == 0)
        {
            return false;
        }

        var what = active.Count == 1 ? active[0].Name : $"{active.Count} transfers";
        if (!await DialogFlow.ConfirmAsync(dialogs, "Cancel transfer",
            $"Stop {what}? What has travelled so far is thrown away and the file is not left at the other end.", "Cancel transfer", destructive: true))
        {
            return false;
        }

        foreach (var transfer in active)
        {
            await SafelyAsync(snackbar, mine, () => api.CancelContainerTransferAsync(transfer.Id));
        }

        return true;
    }

    /// <summary>
    /// The ring's cross: everything this container has on the move or waiting
    /// its turn, asked **batch by batch**. A client chose eight files and sent
    /// them as one act, so undoing it is one question, not eight — and two
    /// batches are two questions, because they were two decisions.
    /// <para>
    /// With more than one batch the question carries the box that answers the
    /// rest the same way, from that answer on, as the overwrite question does.
    /// Each batch says whether it is this client's, so the user knows whose
    /// files they are about to stop.
    /// </para>
    /// </summary>
    public static async Task<bool> StopAsync(IDialogService dialogs, WslcAgentApi api, ISnackbar snackbar,
        FileTransfers mine, IReadOnlyList<ContainerTransfer> transfers)
    {
        // A file nobody announced is a batch of one, which is what it is.
        var batches = transfers
            .Where(transfer => transfer.Active || transfer.IsWaiting)
            .GroupBy(transfer => transfer.Batch.Length > 0 ? transfer.Batch : transfer.Id)
            .Select(batch => batch.ToList())
            .ToList();
        if (batches.Count == 0)
        {
            return false;
        }

        var stopped = false;
        bool? rest = null;
        if (batches.Count > 1)
        {
            // One question for the lot before walking it batch by batch:
            // clearing the queue is the usual reason for reaching for this
            // cross, and making the user answer four questions to do one thing
            // is how a confirmation becomes something people click through.
            var all = batches.Sum(batch => batch.Count);
            if (await DialogFlow.ConfirmAsync(dialogs, "Cancel transfers",
                $"This container has {batches.Count} batches still going or waiting, {all} files in all. Cancel the whole queue?",
                "Cancel them all", destructive: true))
            {
                rest = true;
            }
        }

        foreach (var batch in batches)
        {
            var yes = rest ?? await AskAboutAsync(batch);
            if (!yes)
            {
                continue;
            }

            foreach (var file in batch)
            {
                // One verb for both: the agent stops what is travelling and
                // lets what is waiting out of the queue, and the row says
                // which of the two happened.
                await SafelyAsync(snackbar, mine, () => api.CancelContainerTransferAsync(file.Id));
            }

            stopped = true;
        }

        return stopped;

        async Task<bool> AskAboutAsync(IReadOnlyList<ContainerTransfer> batch)
        {
            var whose = batch.All(file => mine.IsMine(file.Id)) ? "" : " (another client's)";
            // How many were sent and how many are left, which are different
            // numbers once a batch is half through: being asked about "2
            // files" when ten were sent tells the user nothing about what they
            // are looking at.
            var sent = batch.Max(file => file.BatchSize);
            var what = batch.Count == 1 && sent <= 1
                ? $"Stop {batch[0].Name}{whose}?"
                : sent > batch.Count
                    ? $"A batch of {sent} files{whose}, {batch.Count} of them still to go. Stop those {batch.Count}?"
                    : $"{batch.Count} files were sent together{whose}. Stop all of them?";
            var answered = await DialogFlow.AskEachAsync(dialogs, "Cancel transfer",
                $"{what} What has travelled so far is thrown away and the files are not left at the other end.",
                "Cancel transfer", batches.Count > 1 ? "Answer the same for the rest" : "", destructive: true);
            if (answered.ForAll)
            {
                rest = answered.Yes;
            }

            return answered.Yes;
        }
    }

    /// <summary>
    /// Takes files out of the agent's queue before their turn comes. They
    /// never started, so nothing travelled and nothing is thrown away — but it
    /// is asked for all the same: a cross beside a percentage is pressed by
    /// mistake, and a file silently gone from a queue of sixteen is not
    /// noticed until it is missing at the other end.
    /// </summary>
    public static async Task<bool> DropAsync(IDialogService dialogs, FileTransfers transfers, IReadOnlyList<ContainerTransfer> waiting)
    {
        if (waiting.Count == 0)
        {
            return false;
        }

        var what = waiting.Count == 1 ? waiting[0].Name : $"{waiting.Count} files";
        if (!await DialogFlow.ConfirmAsync(dialogs, "Take out of the queue",
            $"Take {what} out of the queue? Nothing of it has travelled, and it will not be sent.", "Take out", destructive: true))
        {
            return false;
        }

        foreach (var file in waiting)
        {
            await transfers.DropAsync(file.Id);
        }

        return true;
    }

    /// <summary>The cross of what has ended: the agent forgets them and the ring goes, for every client. Nothing to confirm, nothing is lost.</summary>
    public static async Task DismissAsync(WslcAgentApi api, ISnackbar snackbar, FileTransfers mine, IReadOnlyList<ContainerTransfer> transfers)
    {
        foreach (var transfer in transfers.Where(transfer => !transfer.Active))
        {
            await SafelyAsync(snackbar, mine, () => api.DismissContainerTransferAsync(transfer.Id));
        }
    }

    private static string Line(ContainerTransfer transfer) =>
        $"{(transfer.IsUpload ? "↑" : "↓")} {transfer.Name} — {(transfer.Error.Length > 0 ? transfer.Error : $"{transfer.Pct}%")}";

    /// <summary>
    /// The agent not answering is not worth a red box on its own: the list is
    /// read again a second later and says how it really is.
    /// <para>
    /// And a transfer the agent no longer has (404) is not a failure either.
    /// Stopping a batch is several orders, and one of its files can go through
    /// — or have gone already — between the list being read and its own order
    /// arriving. Asking for something to stop when it has already stopped is
    /// the state that was asked for, not an error to put on the screen.
    /// </para>
    /// </summary>
    private static async Task SafelyAsync(ISnackbar snackbar, FileTransfers note, Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (AgentApiException gone) when (gone.StatusCode == 404)
        {
            // Already gone, which is where it was being sent.
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            snackbar.Add(ex.Message, Severity.Error);
        }
    }
}
