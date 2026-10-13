using Microsoft.JSInterop;
using MudBlazor;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>What the browser answers of a folder picked on this device (<c>wslcAgent.pickFolder</c>), which it holds by the ticket.</summary>
/// <param name="Name">The folder's name: all a browser says of where it is.</param>
/// <param name="Count">How many files it holds, when the picker read them all; -1 when the folder is read only as its files are wanted.</param>
public sealed record PickedFolder(string Ticket, string Name, int Count)
{
    public static readonly PickedFolder None = new("", "", 0);
}

/// <summary>A file of a build context of a picked folder.</summary>
/// <param name="Index">Its place in the ticket, which is how the browser is asked for it.</param>
/// <param name="Path">Its path from the context, with forward slashes.</param>
/// <param name="Modified">When it was last written, in seconds since 1970.</param>
public sealed record PickedFolderFile(int Index, string Path, long Size, long Modified);

/// <summary>
/// A build context of this device as it was read, with nothing of it sent
/// yet: what there is to send, and the id the agent will know it by.
/// </summary>
/// <param name="Written">The context as the Compose file writes it.</param>
/// <param name="Id">Its id (<see cref="ProjectContextRequest.IdOf"/>): what the file's request names it by.</param>
/// <param name="Entries">Its list: every folder and file, each file with its hash.</param>
/// <param name="First">A file of each hash, by its place in the folder's ticket: the one sent for that hash.</param>
/// <param name="Skipped">The entries the browser would not hand over, which are not in it.</param>
public sealed record ReadContext(string Written, string Id, List<ProjectContextEntry> Entries, IReadOnlyDictionary<string, int> First, List<string> Skipped, int Files, long Bytes);

/// <summary>What the browser answers of a text file chosen on this device (<c>wslcAgent.pickTextFile</c>): its name, and what it holds.</summary>
public sealed record PickedText(string Name, string Text);

/// <summary>What the browser answers of a context of a picked folder (<c>wslcAgent.listPicked</c>).</summary>
/// <param name="Files">Its files.</param>
/// <param name="Skipped">The entries the browser lists and will not hand over (a link whose target is gone, a file the system keeps to itself), by their paths: they are not sent.</param>
public sealed record PickedListing(PickedFolderFile[] Files, string[] Skipped);

/// <summary>
/// The folders a Compose file's services are built from, when the file is one
/// of this device and the agent is on another machine: what the command line
/// does with a folder of its disk (<c>wslc-compose</c>'s own sending), done
/// from a page. The folder is picked in the browser, which is the only one
/// that can read it: the browser lists and hashes the files and sends the
/// ones the agent lacks, and this decides which files those are. A context is
/// what its <c>.dockerignore</c> leaves of its folder, sent as the list of
/// its entries after the files the agent did not have.
/// </summary>
public sealed class LocalBuildContexts(WslcAgentApi api, IJSRuntime js, AgentAccessToken access, ISnackbar snackbar)
{
    /// <summary>Opens the browser's folder chooser; <see cref="PickedFolder.None"/> when nothing was chosen.</summary>
    public async Task<PickedFolder> PickAsync() =>
        await js.InvokeAsync<PickedFolder>("wslcAgent.pickFolder") ?? PickedFolder.None;

    /// <summary>Opens the browser's file chooser for a Compose file of this device; no name when nothing was chosen.</summary>
    public async Task<PickedText> PickTextFileAsync() =>
        await js.InvokeAsync<PickedText>("wslcAgent.pickTextFile") ?? new PickedText("", "");

    /// <summary>Lets the browser go of a folder that is no longer needed.</summary>
    public async Task ReleaseAsync(PickedFolder folder)
    {
        if (folder.Ticket.Length == 0)
        {
            return;
        }

        try
        {
            await js.InvokeVoidAsync("wslcAgent.releasePicked", folder.Ticket);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or OperationCanceledException)
        {
            // The page is going: so is what it held.
        }
    }

    /// <summary>
    /// Reads one build context of the folder: which files it has and what
    /// each is, by the hash of its bytes. It is done on this device and takes
    /// seconds, and from it the context's id is known before any of it is
    /// sent: a save can be asked about, and agreed to, with the sending still
    /// to come. <paramref name="written"/> is the context as the Compose file
    /// writes it (<c>.</c>, <c>./app</c>), from the folder picked, which is
    /// the one the file is in.
    /// </summary>
    public async Task<ReadContext> ReadAsync(PickedFolder folder, string written, Action<string> say)
    {
        say($"Reading build context {written}: listing its files");
        var (files, skipped) = await InContextAsync(folder, written);
        using var read = DotNetObjectReference.Create(new Told((done, bytes) => say($"Reading build context {written}: {done} of {files.Count} files, {Size(bytes)}")));
        var hashes = await js.InvokeAsync<string[]>("wslcAgent.hashPicked", folder.Ticket, files.Select(file => new { index = file.Index, path = file.Path }).ToArray(), read);

        // A file of each hash: several that hold the same bytes are sent once.
        var first = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var at = 0; at < files.Count; at++)
        {
            first.TryAdd(hashes[at], files[at].Index);
        }

        var entries = Entries(files, hashes);
        return new ReadContext(written, ProjectContextRequest.IdOf(entries), entries, first, skipped, files.Count, files.Sum(file => file.Size));
    }

    /// <summary>
    /// Sends a context that was read: the files of it the agent lacks, and
    /// then its list. It may take as long as the line to the agent makes it,
    /// and is done with no window waiting on it: each step says how far it
    /// has got through <paramref name="say"/>, and the sending itself is a
    /// transfer of the agent's, listed under <paramref name="project"/>.
    /// </summary>
    public async Task SendAsync(PickedFolder folder, ReadContext context, string project, Action<string> say)
    {
        var whole = $"{context.Files} files, {Size(context.Bytes)}";
        say($"build context {context.Written} ({whole}): asking the agent what it lacks");
        var missing = await api.GetMissingContextFilesAsync([.. context.First.Keys]);
        if (missing.Count > 0)
        {
            using var going = DotNetObjectReference.Create(new Told((sent, all) => say(sent < all
                ? $"sending build context {context.Written}: {missing.Count} of {context.Files} files, {sent * 100 / Math.Max(all, 1)} % of {Size(all)}"
                : $"build context {context.Written} sent ({Size(all)}), waiting for the agent to keep it")));
            var sent = await js.InvokeAsync<SentFile>("wslcAgent.sendPickedFiles", folder.Ticket,
                missing.Select(hash => new { index = context.First[hash], hash }).ToArray(), api.ContextFilesUploadUrl(project, context.Written, missing.Count).ToString(), access.Value ?? "", going);
            if (!sent.Ok)
            {
                throw new AgentApiException(sent.Status, sent.Error);
            }
        }

        say($"build context {context.Written}: giving the agent its list");
        await api.SendProjectContextAsync(new ProjectContextRequest(context.Entries, context.Skipped, project, context.Written));
    }

    /// <summary>
    /// The files of a context: those of the picked folder under the context's
    /// own folder, each by its path from it, without what the context's
    /// <c>.dockerignore</c> leaves out, in the order of their names. A folder
    /// the file leaves out whole is not gone into at all, where the browser
    /// walks the folder as it is asked: a virtual environment of forty
    /// thousand files is then never read.
    /// </summary>
    private async Task<(List<PickedFolderFile> Files, List<string> Skipped)> InContextAsync(PickedFolder folder, string written)
    {
        var under = written.Replace('\\', '/').Trim('/');
        while (under.StartsWith("./", StringComparison.Ordinal))
        {
            under = under[2..];
        }

        under = under == "." ? "" : under;
        if (under.Split('/').Contains(".."))
        {
            throw new InvalidOperationException($"The build context {written} is outside the folder picked on this device. Pick the folder the Compose file is in, or one above it that holds the context.");
        }

        var ignore = DockerIgnore.Parse(await js.InvokeAsync<string>("wslcAgent.pickedText", folder.Ticket, (under.Length > 0 ? under + "/" : "") + DockerIgnore.FileName));
        using var gate = DotNetObjectReference.Create(new Gate(ignore.Enters));
        var listed = await js.InvokeAsync<PickedListing>("wslcAgent.listPicked", folder.Ticket, under, gate);
        if (listed.Files.Length == 0)
        {
            throw new InvalidOperationException($"The folder picked on this device ({folder.Name}) has no {written}, or nothing in it: pick the folder the Compose file is in.");
        }

        // Said and not hidden: a build that needed one of them fails later for a file that is not
        // there. Said here, in a warning that stays until it is closed, and to the agent, which
        // writes it in its log.
        var skipped = listed.Skipped.Where(name => !ignore.Leaves(name.TrimEnd('/'))).ToList();
        if (skipped.Count > 0)
        {
            snackbar.Add($"Build context {written}: {skipped.Count} entries of the folder could not be read and are not sent: {string.Join(", ", skipped.Take(8))}{(skipped.Count > 8 ? ", …" : "")}. The agent's log has them all.", Severity.Warning);
        }

        return (listed.Files.Where(file => !ignore.Leaves(file.Path)).OrderBy(file => file.Path, InFolderOrder.Instance).ToList(), skipped);
    }

    /// <summary>
    /// The context's list: every file with its hash, and before the files of
    /// a folder the folder itself, which a browser does not say and is made
    /// from the paths of what is in it.
    /// </summary>
    private static List<ProjectContextEntry> Entries(List<PickedFolderFile> files, string[] hashes)
    {
        var entries = new List<ProjectContextEntry>();
        var folders = new HashSet<string>(StringComparer.Ordinal);
        for (var at = 0; at < files.Count; at++)
        {
            var name = files[at].Path;
            for (var slash = name.IndexOf('/'); slash > 0; slash = name.IndexOf('/', slash + 1))
            {
                if (folders.Add(name[..slash]))
                {
                    entries.Add(new ProjectContextEntry(name[..slash], 0, files[at].Modified, ""));
                }
            }

            entries.Add(new ProjectContextEntry(name, files[at].Size, files[at].Modified, hashes[at]));
        }

        return entries;
    }

    private static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0.0} KB",
        _ => $"{bytes} bytes",
    };

    /// <summary>
    /// The order a folder is listed in: by the names along each path, one
    /// folder's entries together and before the next one's, so a folder
    /// stands just before what is in it. Comparing the paths as texts would
    /// put <c>a.txt</c> between <c>a</c> and <c>a/b</c>.
    /// </summary>
    private sealed class InFolderOrder : IComparer<string>
    {
        public static readonly InFolderOrder Instance = new();

        public int Compare(string? left, string? right)
        {
            var (a, b) = ((left ?? "").Split('/'), (right ?? "").Split('/'));
            for (var at = 0; at < Math.Min(a.Length, b.Length); at++)
            {
                if (string.CompareOrdinal(a[at], b[at]) is not 0 and var order)
                {
                    return order;
                }
            }

            return a.Length.CompareTo(b.Length);
        }
    }

    /// <summary>What the browser tells as it goes: two counts, whose meaning is the step's.</summary>
    private sealed class Told(Action<long, long> tell)
    {
        [JSInvokable]
        public void Progress(long first, long second) => tell(first, second);
    }

    /// <summary>What the browser asks as it walks a folder: whether to go into one under it.</summary>
    private sealed class Gate(Func<string, bool> enters)
    {
        [JSInvokable]
        public bool Enters(string name) => enters(name);
    }
}
