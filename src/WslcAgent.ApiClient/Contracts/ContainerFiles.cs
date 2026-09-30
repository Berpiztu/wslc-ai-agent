namespace WslcAgent.ApiClient.Contracts;

/// <summary>One entry of a directory inside a container, as <c>ls -la</c> reports it.</summary>
/// <param name="Name">File name, without its directory.</param>
/// <param name="Type"><c>dir</c>, <c>file</c>, <c>link</c> or <c>other</c>.</param>
/// <param name="Mode">The permission string (<c>drwxr-xr-x</c>).</param>
/// <param name="Size">Bytes; a directory's own size, as the shell reports it.</param>
/// <param name="SizeHuman">The same size for a person to read.</param>
/// <param name="Mtime">Last change, in the shell's own words (<c>Sep 15 08:55</c>).</param>
/// <param name="Target">Where a symbolic link points, empty otherwise.</param>
/// <param name="Path">Absolute path inside the container.</param>
public sealed record ContainerFileEntry(
    string Name,
    string Type,
    string Mode,
    long Size,
    string SizeHuman,
    string Owner,
    string Group,
    string Mtime,
    string Target,
    string Path)
{
    public const string Directory = "dir";
    public const string File = "file";
    public const string Link = "link";

    public bool IsDirectory => Type == Directory;
}

/// <summary>A directory listing: where it is, what is above it, and what is in it (directories first, then by name).</summary>
public sealed record ContainerFileListing(string Path, string Parent, IReadOnlyList<ContainerFileEntry> Entries);

/// <summary>A text file read for the editor.</summary>
public sealed record ContainerFileContent(string Path, string Content, long Size);

/// <summary>Body of <c>PUT /api/v1/containers/{id}/files/content</c>.</summary>
public sealed record WriteFileRequest(string Path, string Content = "");

/// <summary>Body of <c>POST /api/v1/containers/{id}/files/mkdir</c>.</summary>
public sealed record MakeDirectoryRequest(string Path);

/// <summary>Body of the rename and copy endpoints: one path to another, both inside the container.</summary>
public sealed record MoveFileRequest(string Source, string Destination);

/// <summary>
/// Answer of <c>POST /api/v1/images/files-session</c> and <c>POST /api/v1/volumes/{name}/files-session</c>:
/// a temporary helper container whose files the Files view browses with the container
/// files endpoints, from <paramref name="Root"/> down (<c>/</c> for an image, the
/// volume's mount point for a volume).
/// </summary>
public sealed record FilesSession(string Container, string Root);

/// <summary>Body of <c>POST /api/v1/images/files-session</c>.</summary>
public sealed record ImageFilesRequest(string Reference);

/// <summary>
/// A file on its way in or out of a container, as the agent sees it: a
/// transfer is a job the agent owns, so the Files view that started it can
/// close and the container's row keeps the ring, the reason it failed and the
/// cross that stops it — the launches' contract for bytes instead of a run.
/// <para>
/// A container can have several at once, from several clients, and each file
/// of a selection is one of these: the row adds up the ones still going and
/// shows a single ring for all of them.
/// </para>
/// <para>
/// What the percentage measures is what can be measured. Going in, it is the
/// bytes the agent has received, because a browser never says how much of a
/// request it has sent, and the copy into the container has no measure at all,
/// so the ring holds at the top and the status says what it is doing. Coming
/// out, the two halves are halves of the ring, as a pull's download and
/// extraction are: the copy out of the container fills the first fifty (the
/// staged file is watched as it grows), the send to the client the rest.
/// </para>
/// </summary>
/// <param name="Id">The job's id, for cancelling and dismissing it.</param>
/// <param name="Container">The container as the client named it: its id or its name.</param>
/// <param name="Direction"><c>in</c> (an upload) or <c>out</c> (a download).</param>
/// <param name="Directory">The folder the file goes into, or comes from.</param>
/// <param name="Name">The file's name, as it is on either side.</param>
/// <param name="Phase"><c>waiting</c>, <c>receive</c>, <c>copy</c>, <c>send</c>, <c>done</c>, <c>cancelled</c> or <c>error</c>.</param>
/// <param name="Bytes">What the stage under way has done so far.</param>
/// <param name="TotalBytes">The file's size; 0 when nobody has said it yet, and then there is no percentage to give.</param>
/// <param name="Batch">The announcement this file came in, empty for a transfer nobody queued; a batch is cancelled whole and expires whole.</param>
/// <param name="Position">How many of its own direction's queue stand before it, the file on its way included: 0 when its turn has come, and for anything already travelling.</param>
/// <param name="BatchSize">
/// How many files its announcement had, which the ones already through no
/// longer say: a batch of ten with two left is a different thing to ask about
/// than a batch of two, and the agent is the only one that still knows.
/// </param>
public sealed record ContainerTransfer(
    string Id,
    string Container,
    string Direction,
    string Directory,
    string Name,
    string Phase,
    string Status,
    int Pct,
    long Bytes,
    long TotalBytes,
    string Error,
    string Batch = "",
    int Position = 0,
    int BatchSize = 0)
{
    /// <summary>Into the container: an upload.</summary>
    public const string In = "in";

    /// <summary>Out of the container: a download.</summary>
    public const string Out = "out";

    /// <summary>
    /// Announced and waiting its turn in the agent's queue, with nothing
    /// travelling yet: the client said it would send
    /// this file, and it sends it when this is the head of the queue.
    /// </summary>
    public const string Waiting = "waiting";

    /// <summary>Bytes arriving from the client (an upload).</summary>
    public const string Receiving = "receive";

    /// <summary><c>wslc container cp</c> moving the file between the agent and the container.</summary>
    public const string Copying = "copy";

    /// <summary>Bytes going to the client (a download).</summary>
    public const string Sending = "send";

    /// <summary>The file is through.</summary>
    public const string Done = "done";

    /// <summary>Someone's cross stopped it, or the client carrying it went.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>It broke, and <see cref="Error"/> says how.</summary>
    public const string Failed = "error";

    public bool Active => Phase is Receiving or Copying or Sending;

    /// <summary>Its turn has not come: it is in the queue and nothing of it has travelled.</summary>
    public bool IsWaiting => Phase == Waiting;

    /// <summary>Worth showing on a row or in a list: on its way, waiting its turn, or ended badly and not yet dismissed.</summary>
    public bool IsShowing => Active || IsWaiting || Phase is Failed or Cancelled;

    public bool IsUpload => Direction == In;

    /// <summary>The file's path inside the container, for a row that has to say where it is going or coming from.</summary>
    public string Path => Directory.EndsWith('/') ? Directory + Name : $"{Directory}/{Name}";
}

/// <summary>One file a client says it is going to move, before anything of it travels.</summary>
/// <param name="Name">Its name on either side, which is the name it will have in the container.</param>
/// <param name="Size">What it measures, so the queue can say how much is ahead and the ring has a percentage.</param>
public sealed record AnnouncedFile(string Name, long Size);

/// <summary>
/// A batch a client announces before it sends or fetches anything:
/// this container, this direction, these files. The
/// agent appends them to its queue in this order and answers with the ids that
/// make them the client's own.
/// </summary>
/// <param name="Overwrite">
/// The names the user has already agreed to write over. A batch with a file
/// that is there and is not in this list queues nothing at all and comes back
/// as <see cref="AnnouncedTransfers.Clashes"/>: the agent is the one that
/// knows what is in the container, and it says so before a byte travels
/// rather than after, when the turn may come with nobody at the screen.
/// </param>
public sealed record AnnounceTransfersRequest(
    string Direction,
    string Directory,
    IReadOnlyList<AnnouncedFile> Files,
    IReadOnlyList<string>? Overwrite = null);

/// <summary>
/// What the agent answers an announcement with: the batch's id and one id per
/// file, in the order they were announced. The client keeps them — they are
/// what lets it tell its own files from its neighbour's, and what it sends
/// each file under when its turn comes.
/// </summary>
/// <param name="Clashes">
/// The announced names that are already in that folder and were not agreed
/// to. When there are any, nothing was queued — not even the files that would
/// have been fine — and the client asks about each of them and announces
/// again. Empty on a batch that took its places.
/// </param>
public sealed record AnnouncedTransfers(string Batch, IReadOnlyList<ContainerTransfer> Files, IReadOnlyList<string>? Clashes = null)
{
    /// <summary>Nothing was queued because these are already there and nobody has said what to do about them.</summary>
    public IReadOnlyList<string> Existing => Clashes ?? [];
}
