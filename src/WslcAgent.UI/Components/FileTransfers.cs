using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Files;

namespace WslcAgent.UI.Components;

/// <summary>One of the files a picker answered with, kept by the place it was answered in so each is sent for itself.</summary>
public sealed record PickedFile(int Index, string Name, long Size);

/// <summary>
/// What one pick answered: the files, and the ticket the browser is holding
/// them under. The ticket, not the input element they came from — the view
/// that owns that element closes, and a file waiting its turn must not go
/// with it.
/// </summary>
public sealed record PickedFiles(string Ticket, IReadOnlyList<PickedFile> Files)
{
    public static readonly PickedFiles None = new("", []);
}

/// <summary>What the browser made of an upload it sent (<c>wslcAgent.sendFile</c>).</summary>
public sealed record SentFile(bool Ok, int Status, string Error);

/// <summary>
/// The files this client is moving in or out of containers, held by the
/// application and not by the screen that asked: the Files view closes, the
/// user walks to another page, and the bytes keep going.
/// <para>
/// The queue is the agent's, not this client's. A
/// batch is announced before anything of it travels, the agent answers with an
/// id for the batch and one per file, and each file is sent when its id is the
/// head of the agent's queue. So the waiting files are everybody's to see and
/// anybody's to take out — what was invisible to every client but the one
/// holding them — and two clients cannot send at once by accident.
/// </para>
/// <para>
/// What is left here is the carrying and the holding: the bytes are in this
/// browser or on this machine, and nobody else can send them. Who carries
/// depends on what can. Going up, the web UI hands the file to the browser
/// (<see cref="BrowserSends"/>), because .NET in WebAssembly cannot stream a
/// request and would hold a whole backup in memory, twice; the native client
/// picks the file through the OS and its own <c>HttpClient</c> streams it from
/// the disk it is on. Coming down, the browser downloads as browsers do, and
/// the native client, whose WebView has no download UI, writes the file
/// through the OS picker — several at once into one folder chosen once.
/// Neither way goes through the WebView's own file input: what it answers is
/// good only while the screen that asked is still on, and a transfer outlives
/// the screen by design.
/// </para>
/// <para>
/// Neither can outlive the page: a client reloaded while bytes are still
/// travelling takes its request with it, and the agent's job then ends as
/// cancelled and says so. What it had announced and not yet sent loses its
/// turn and is let go by the agent's timeout.
/// </para>
/// </summary>
public sealed class FileTransfers(WslcAgentApi api, ISnackbar snackbar, IDialogService dialogs, IJSRuntime js,
    AgentAccessToken access, IClientFiles clientFiles, ILogger<FileTransfers> log)
{
    /// <summary>Bytes with nothing to say about what they are: what a container's file is to the machine saving it.</summary>
    private const string Bytes = "application/octet-stream";

    /// <summary>How often a file that is waiting asks the agent whether its turn has come.</summary>
    private static readonly TimeSpan AskAgain = TimeSpan.FromMilliseconds(700);

    /// <summary>
    /// The files this client announced and has not finished with, by the id
    /// the agent gave each — waiting their turn or travelling alike. It is what
    /// makes them its own: a container's screens show a cross on a file only
    /// when it is in here, and everything else in the agent's queue belongs to
    /// somebody else.
    /// </summary>
    private readonly Dictionary<string, string> _mine = new(StringComparer.Ordinal);

    /// <summary>Raised when a transfer of this client ends — container, then folder — for a view still showing where it went.</summary>
    public event Action<string, string>? Finished;

    /// <summary>Raised whenever what this client holds changes, for a screen watching it.</summary>
    public event Action? QueueChanged;

    /// <summary>The web UI sends its files through the browser; the native client sends them itself.</summary>
    public static bool BrowserSends => OperatingSystem.IsBrowser();

    /// <summary>
    /// Writes down what is not worth a line on the screen but is worth a trace:
    /// a file somebody cancelled, an order for something that had already
    /// stopped. None of it is news to the person who did it, and all of it is
    /// what a question a week later is answered with.
    /// </summary>
    public void Note(string what) => log.LogInformation("transfers: {What}", what);

    /// <summary>Whether this client is still carrying something in or out of that container, so a view showing it follows the agent's count.</summary>
    public bool Busy(string container) => _mine.Values.Any(where => where == container);

    /// <summary>
    /// Whether that file of the agent's queue is one this client announced.
    /// Only its own may be taken out of the queue from a container's screens;
    /// the dashboard's card is the administrator's view and takes out any.
    /// </summary>
    public bool IsMine(string id) => _mine.ContainsKey(id);

    /// <summary>
    /// Takes a file out of the agent's queue before its turn comes. It never
    /// started, so nothing is stopped: it is the client saying it will not send
    /// that one, and every client sees it go.
    /// </summary>
    public async Task DropAsync(string id)
    {
        try
        {
            await api.CancelContainerTransferAsync(id);
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            snackbar.Add($"Could not take it out of the queue. {ex.Message}", Severity.Error);
        }
    }

    /// <summary>Opens the picker of the view's own file input; empty when nothing was chosen. Several at once are allowed, and each becomes a transfer of its own.</summary>
    public async Task<PickedFiles> PickAsync(ElementReference input) =>
        await js.InvokeAsync<PickedFiles>("wslcAgent.pickFiles", input) ?? PickedFiles.None;

    /// <summary>Opens the native client's own picker; empty when nothing was chosen. Several at once are allowed, and each becomes a transfer of its own.</summary>
    public Task<IReadOnlyList<IClientFile>> PickOnClientAsync() => clientFiles.PickFilesAsync();

    /// <summary>
    /// The web UI's upload: the files a pick is holding, announced as one batch
    /// and sent by the browser one at a time as their turns come. It returns at
    /// once — the upload outlives the screen that started it.
    /// </summary>
    public void Send(string container, string directory, PickedFiles chosen)
    {
        if (chosen.Files.Count == 0)
        {
            return;
        }

        _ = CarryAsync(container, ContainerTransfer.In, directory,
            [.. chosen.Files.Select(file => new Carrying(file.Name, file.Size, async id =>
            {
                var sent = await js.InvokeAsync<SentFile>("wslcAgent.sendFile", chosen.Ticket, file.Index,
                    api.ContainerFileUploadUrl(container, directory, file.Size, id).ToString(), access.Value ?? "");
                if (!sent.Ok)
                {
                    // The browser's answer, made into the one failure every caller here knows.
                    throw new AgentApiException(sent.Status, sent.Error);
                }
            }))], "upload ok");
    }

    /// <summary>The native client's own upload, streamed by .NET from each file's own path, under the same queue and the same turns.</summary>
    public void Start(string container, string directory, IReadOnlyList<IClientFile> files)
    {
        if (files.Count == 0)
        {
            return;
        }

        _ = CarryAsync(container, ContainerTransfer.In, directory,
            [.. files.Select(file => new Carrying(file.Name, file.Size, async id =>
            {
                // Opened when its turn comes, not when it was picked: a file of
                // the OS is still there, and nothing here holds its bytes.
                await using var content = await file.OpenReadAsync();
                await api.UploadContainerFileAsync(container, directory, file.Name, content, file.Size, id);
            }))], "upload ok");
    }

    /// <summary>
    /// Brings files out of the container: the browser does its own downloading,
    /// one tab each, and a native client writes them where the user says. Every
    /// file is a transfer of its own on the agent, and the container's row adds
    /// them up into one ring.
    /// </summary>
    public void Download(string container, string directory, IReadOnlyList<ContainerFileEntry> files)
    {
        if (files.Count == 0)
        {
            return;
        }

        if (!clientFiles.Supported)
        {
            _ = OpenInBrowserAsync(container, files);
            return;
        }

        // The folder is asked for once and the files are written into it one
        // after another, so the batch is announced as one and its files take
        // their turns inside it: a picker that opened again between two files
        // would be worse than waiting.
        _ = SaveAsync(container, directory, files);
    }

    /// <summary>
    /// The browser's own download, which shows its own progress and lands in
    /// its own downloads folder. A tab each rather than a navigation: an
    /// attachment closes the tab it opened, and a refusal shows there instead
    /// of throwing the application off the page it was on.
    /// <para>
    /// These are in no queue, and never were: once a download is handed to the
    /// browser it is the browser's to run, and a tab asked for later than the
    /// click that asked for it is a tab the browser blocks.
    /// </para>
    /// </summary>
    private Task OpenInBrowserAsync(string container, IReadOnlyList<ContainerFileEntry> files)
    {
        // Every tab is asked for in the same pass, before anything is awaited:
        // a browser opens the windows a click asked for and blocks the ones
        // that come after it, so a selection of six would have arrived as one
        // file and five blocked pop-ups.
        var opening = files
            .Select(file => js.InvokeVoidAsync("window.open", api.ContainerFileUrl(container, file.Path).ToString(), "_blank").AsTask())
            .ToList();
        return Task.WhenAll(opening);
    }

    /// <summary>
    /// The native client's download: one Save As for one file, one folder for
    /// several, and the files written into it as their turns come. No line of
    /// its own when it is through — a save says where it landed, which is the
    /// thing the user wants to read.
    /// </summary>
    private async Task SaveAsync(string container, string directory, IReadOnlyList<ContainerFileEntry> files)
    {
        if (files.Count == 1)
        {
            string? saved = null;
            await CarryAsync(container, ContainerTransfer.Out, directory,
                [new Carrying(files[0].Name, files[0].Size, async id =>
                    saved = await clientFiles.SaveStreamAsync(files[0].Name, Bytes,
                        (stream, token) => api.DownloadContainerFileAsync(container, files[0].Path, stream, id, token)))],
                done: null);
            if (saved is not null)
            {
                snackbar.Add($"Saved to {saved}", Severity.Success);
            }

            return;
        }

        if (await clientFiles.PickFolderAsync() is not { } folder)
        {
            return;
        }

        await CarryAsync(container, ContainerTransfer.Out, directory,
            [.. files.Select(file => new Carrying(file.Name, file.Size, id =>
                folder.WriteAsync(file.Name, Bytes, (stream, token) => api.DownloadContainerFileAsync(container, file.Path, stream, id, token))))],
            done: null);
        snackbar.Add($"{files.Count} files saved to {folder.Display}", Severity.Success);
    }

    /// <summary>
    /// One batch, from its announcement to its last file. The agent is told
    /// what is coming, answers with the ids, and each file goes when its id is
    /// the head of the queue: no two clients send at once, and everyone can see
    /// and stop what is waiting.
    /// <para>
    /// <paramref name="done"/> is the word each file's own line says when it is
    /// through, and null for a download: a save says where it landed, which is
    /// the thing the user wants to read, and two lines for one file is one too
    /// many.
    /// </para>
    /// </summary>
    private async Task CarryAsync(string container, string direction, string directory, IReadOnlyList<Carrying> files, string? done)
    {
        var (batch, going) = await AnnounceAsync(container, direction, directory, files);
        if (batch is null)
        {
            return;
        }

        files = going;

        foreach (var announced in batch.Files)
        {
            _mine[announced.Id] = container;
        }

        QueueChanged?.Invoke();
        try
        {
            for (var index = 0; index < files.Count && index < batch.Files.Count; index++)
            {
                await CarryOneAsync(container, directory, batch.Files[index].Id, files[index], done);
            }
        }
        finally
        {
            // Whatever is left was never sent — the page is going, or a file
            // failed and the rest never had their turn: the agent's timeout
            // lets them go, and this client stops calling them its own.
            foreach (var announced in batch.Files)
            {
                _mine.Remove(announced.Id);
            }

            QueueChanged?.Invoke();
        }
    }

    /// <summary>
    /// Announces the batch, and asks about each file that is already there
    /// before anything of it is queued. The agent is what knows a folder's
    /// contents — a screen's listing is as old as the last time it read it —
    /// and it answers with the names that clash instead of queueing anything,
    /// so nothing takes a turn while the user is being asked.
    /// <para>
    /// One question per file: a
    /// batch is the rare case, and the common one is a single file, where
    /// being asked once is no burden at all. Saying no to one leaves it behind
    /// and the rest go; saying no to all of them leaves nothing to announce.
    /// </para>
    /// </summary>
    private async Task<(AnnouncedTransfers? Batch, IReadOnlyList<Carrying> Files)> AnnounceAsync(
        string container, string direction, string directory, IReadOnlyList<Carrying> files)
    {
        List<string> agreed = [];
        while (true)
        {
            if (files.Count == 0)
            {
                return (null, files);
            }

            AnnouncedTransfers batch;
            try
            {
                batch = await api.AnnounceContainerTransfersAsync(container,
                    new AnnounceTransfersRequest(direction, directory, [.. files.Select(file => new AnnouncedFile(file.Name, file.Size))], agreed));
            }
            catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
            {
                snackbar.Add($"The agent would not take the files. {ex.Message}", Severity.Error);
                return (null, files);
            }

            if (batch.Existing.Count == 0)
            {
                return (batch, files);
            }

            var skipped = new HashSet<string>(StringComparer.Ordinal);
            bool? rest = null;
            foreach (var name in batch.Existing)
            {
                // Once the box has been ticked, the rest take that answer
                // without being asked. The rest: what was answered before it
                // keeps the answer it was given, so five refused one by one
                // stay refused when the sixth is accepted for all.
                var yes = rest ?? await Ask(name, batch.Existing.Count);
                if (yes)
                {
                    agreed.Add(name);
                }
                else
                {
                    skipped.Add(name);
                }
            }

            files = [.. files.Where(file => !skipped.Contains(file.Name))];

            async Task<bool> Ask(string name, int howMany)
            {
                var answered = await DialogFlow.AskEachAsync(dialogs, "The file is already there",
                    $"{name} is already in {directory}. Write over it?", "Overwrite",
                    howMany > 1 ? "Answer the same for the rest" : "", destructive: true);
                if (answered.ForAll)
                {
                    rest = answered.Yes;
                }

                return answered.Yes;
            }
        }
    }

    /// <summary>One file of a batch: its turn, its bytes, and what became of it.</summary>
    private async Task CarryOneAsync(string container, string directory, string id, Carrying file, string? done)
    {
        try
        {
            if (!await MyTurnAsync(id))
            {
                // It left the queue while it waited: somebody's cross took it
                // out, or its turn came and went with this client not looking
                // and the agent let the batch go. Either way it is not ours to
                // send any more, and the row it had already said so.
                return;
            }

            // It stays this client's while it travels, and not only while it
            // waits: a file is no less mine for having started, and letting go
            // of it here left me unable to stop my own upload from the screen
            // that started it.
            QueueChanged?.Invoke();
            await file.Send(id);
            if (done is not null)
            {
                snackbar.Add($"{file.Name}: {done}", Severity.Success);
            }
        }
        catch (Exception cancelled) when (cancelled is OperationCanceledException or AgentApiException { StatusCode: 404 or 409 })
        {
            // Somebody's cross stopped it: here or on another client, before
            // its turn or halfway through. 404 is the agent saying it is no
            // longer in the queue — the file was taken out between this client
            // checking its turn and sending it — and 409 that it was stopped
            // while it travelled; a browser that was interrupted throws its
            // own. None of them is a failure, and none of them is news: the
            // person who pressed the cross knows, the row says so to everyone
            // else, and a line of its own for each of eight cancelled files is
            // eight lines saying what was already decided. It goes to the log
            // all the same, where a trace costs nobody anything.
            Note($"{file.Name}: cancelled ({cancelled.Message})");
        }
        catch (Exception exception) when (exception is AgentApiException or HttpRequestException or IOException or JSException or InvalidOperationException)
        {
            // The agent's row says the same thing to everyone; this says it to
            // the one who asked, wherever they are now.
            snackbar.Add($"{file.Name}: transfer failed. {exception.Message}", Severity.Error);
        }
        finally
        {
            _mine.Remove(id);
            QueueChanged?.Invoke();
            Finished?.Invoke(container, directory);
        }
    }

    /// <summary>
    /// Waits until this file is the head of its queue and it is this client's
    /// turn to send. False when the file is no longer in the queue at all: it
    /// was taken out, or its whole batch was let go, and there is nothing left
    /// to send.
    /// </summary>
    private async Task<bool> MyTurnAsync(string id)
    {
        while (true)
        {
            IReadOnlyList<ContainerTransfer> queue;
            try
            {
                queue = await api.GetContainerTransfersAsync();
            }
            catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
            {
                // The agent did not answer this time; asking again is the whole
                // of the recovery, and its own timeout is what ends the wait if
                // it never answers.
                await Task.Delay(AskAgain);
                continue;
            }

            if (queue.FirstOrDefault(transfer => transfer.Id == id) is not { } mine)
            {
                return false;
            }

            if (!mine.IsWaiting)
            {
                // Somebody is already carrying it, which cannot be this client:
                // it is not ours to send twice.
                return false;
            }

            if (mine.Position == 0)
            {
                return true;
            }

            await Task.Delay(AskAgain);
        }
    }

    /// <summary>One file this client is holding: what it is called, what it measures, and how it is sent once its turn comes.</summary>
    private sealed record Carrying(string Name, long Size, Func<string, Task> Send);
}
