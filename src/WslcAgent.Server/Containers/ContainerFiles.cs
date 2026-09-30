using System.Diagnostics;
using System.Text;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Resources;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// The files inside a container: there is no
/// wslc file API, so listing is <c>ls -la</c> through <c>exec</c>, the writes
/// are <c>mkdir</c>, <c>mv</c>, <c>cp</c> and <c>rm</c>, and the bytes travel
/// with <c>wslc container cp</c>. Every path is made absolute and clean before
/// it reaches a command, and each value is its own argument, so a name can
/// never turn into a flag or a second command.
/// </summary>
public sealed class ContainerFiles(IWslcRunner wslc, ResourceRegistry registry, ILogger<ContainerFiles> logger)
{
    /// <summary>The editor is for configuration files, not for images: a megabyte at most.</summary>
    public const int MaxEditBytes = 1_000_000;

    /// <summary>wslc refuses every exec against a stopped container with this code; the text is translated, the code is not.</summary>
    private const string NotRunning = "WSLC_E_CONTAINER_NOT_RUNNING";

    private static readonly TimeSpan CopyTimeout = TimeSpan.FromMinutes(10);

    /// <summary>How often a copy with no voice of its own is measured by what it has written.</summary>
    private static readonly TimeSpan StagingTick = TimeSpan.FromMilliseconds(250);

    public async Task<ContainerFileListing> ListAsync(string container, string? path, CancellationToken cancellationToken = default)
    {
        var id = WslcArgs.Require(container, "container");
        var directory = ContainerPath.Clean(path);

        // One exec per folder opened, not two: every exec is a wslc process
        // and a trip into the container, and a probe before each listing
        // doubled the wait of every click. The trailing "/." makes ls refuse
        // a file (and follow a link to a folder), so a listing that answers
        // is a folder's; only one that fails pays for the probes that say why
        // — a stopped container, a missing path, or a file.
        WslcResult result;
        try
        {
            result = await Exec(id, ["ls", "-la", "--", directory.TrimEnd('/') + "/."], cancellationToken);
        }
        catch (WslcException ex)
        {
            if (IsStopped(ex))
            {
                throw NotRunningError();
            }

            if (!await TestAsync(id, "-d", directory, cancellationToken))
            {
                throw await ExplainAsync(id, directory, cancellationToken);
            }

            throw;
        }
        var entries = LsParsing.Entries(result.Stdout, directory)
            .OrderBy(entry => entry.Type switch { ContainerFileEntry.Directory => 0, ContainerFileEntry.Link => 1, _ => 2 })
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ContainerFileListing(directory, ContainerPath.Parent(directory), entries);
    }

    /// <summary>The file's text for the editor; a directory or an oversized file is refused before it is read.</summary>
    public async Task<ContainerFileContent> ReadAsync(string container, string path, CancellationToken cancellationToken = default)
    {
        var id = WslcArgs.Require(container, "container");
        var file = ContainerPath.File(path);
        using var titled = Titled(id, "Read", ContainerPath.Name(file), $"← {ContainerPath.Parent(file)}");
        if (!await TestAsync(id, "-f", file, cancellationToken))
        {
            throw await ExplainAsync(id, file, cancellationToken);
        }

        var staged = Staging.Path($"read-{id}", ContainerPath.Name(file));
        try
        {
            await CopyOutAsync(id, file, staged, cancellationToken);
            var bytes = await File.ReadAllBytesAsync(staged, cancellationToken);
            if (bytes.Length > MaxEditBytes)
            {
                throw new InvalidOperationException($"{ContainerPath.Name(file)} is {bytes.Length} bytes; the editor opens files up to {MaxEditBytes}.");
            }

            return new ContainerFileContent(file, Encoding.UTF8.GetString(bytes), bytes.Length);
        }
        finally
        {
            Staging.Discard(staged);
        }
    }

    /// <summary>Writes the text back, creating the file or replacing it.</summary>
    public async Task<ContainerFileEntry> WriteAsync(string container, string path, string content, CancellationToken cancellationToken = default)
    {
        var id = WslcArgs.Require(container, "container");
        var file = ContainerPath.File(path);
        var bytes = Encoding.UTF8.GetBytes(content ?? "");
        if (bytes.Length > MaxEditBytes)
        {
            throw new InvalidOperationException($"The text is {bytes.Length} bytes; the editor writes files up to {MaxEditBytes}.");
        }

        var name = ContainerPath.Name(file);
        using var titled = Titled(id, "Save", name, $"→ {ContainerPath.Parent(file)}", Bytes.Humanize(bytes.Length));
        var staged = Staging.Path($"write-{id}", Travels(name) ? name : Travelling());
        try
        {
            await File.WriteAllBytesAsync(staged, bytes, cancellationToken);
            // An existing destination makes container cp fail: the old file goes first.
            await TryExec(id, ["rm", "-f", "--", file], cancellationToken);
            await CopyInAsync(id, staged, ContainerPath.Parent(file), name, cancellationToken);
        }
        finally
        {
            Staging.Discard(staged);
        }

        return await StatAsync(id, file, cancellationToken);
    }

    /// <summary>
    /// Copies the file out of the container so the client can download it; the
    /// caller deletes it. <paramref name="progress"/> is told the file's size
    /// and then watches the staged copy grow: <c>wslc container cp</c> says
    /// nothing at all while it works, and a gigabyte coming out of a container
    /// is minutes of a screen with nothing on it.
    /// </summary>
    public async Task<(string Staged, string Name)> StageForDownloadAsync(string container, string path, ITransferProgress? progress = null, CancellationToken cancellationToken = default)
    {
        var id = WslcArgs.Require(container, "container");
        var file = ContainerPath.File(path);
        var name = ContainerPath.Name(file);
        var from = $"← {ContainerPath.Parent(file)}";
        var staged = Staging.Path($"download-{id}", name);
        long? size = null;
        if (progress is not null)
        {
            using (Titled(id, "Download", name, from))
            {
                size = (await StatAsync(id, file, cancellationToken)).Size;
            }

            progress.Total(size.Value);
        }

        using var titled = Titled(id, "Download", name, from, size is { } bytes ? Bytes.Humanize(bytes) : null);
        await WatchAsync(CopyOutAsync(id, file, staged, cancellationToken), staged, progress);
        if (!File.Exists(staged))
        {
            throw new InvalidOperationException($"{file} could not be copied out of the container.");
        }

        return (staged, ContainerPath.Name(file));
    }

    /// <summary>
    /// Runs a copy that reports nothing and tells <paramref name="progress"/>
    /// how big the staged file has become while it runs. The copy's own
    /// failure is what this throws: the watching is only watching.
    /// </summary>
    private static async Task WatchAsync(Task copy, string staged, ITransferProgress? progress)
    {
        if (progress is null)
        {
            await copy;
            return;
        }

        // The tick takes no token: what ends this is the copy ending, and the
        // copy is the one that carries the cancellation.
        while (await Task.WhenAny(copy, Task.Delay(StagingTick)) != copy)
        {
            progress.Progress(File.Exists(staged) ? new FileInfo(staged).Length : 0);
        }

        await copy;
        progress.Progress(File.Exists(staged) ? new FileInfo(staged).Length : 0);
    }

    /// <summary>
    /// Puts a file the user uploaded into a directory of the container, keeping
    /// its name. <paramref name="progress"/> is told how much has arrived and
    /// when the copy into the container starts, for the job the row watches
    /// (<see cref="ContainerTransfers"/>); nothing here knows about that job.
    /// </summary>
    public async Task<ContainerFileEntry> UploadAsync(string container, string directory, string name, Stream content, ITransferProgress? progress = null, CancellationToken cancellationToken = default)
    {
        var id = WslcArgs.Require(container, "container");
        var target = ContainerPath.Clean(directory);
        var file = ContainerPath.Name(name);
        if (file.Length == 0)
        {
            throw new ArgumentException("A file name is required.", nameof(name));
        }

        var staged = Staging.Path($"upload-{id}", Travels(file) ? file : Travelling());
        try
        {
            // The time the client took to send it, which is what the transfer
            // took for whoever sent it: the copy into the container after it is
            // the command's own row, and only the last step.
            var sending = Stopwatch.StartNew();
            await using (var destination = File.Create(staged))
            {
                await ReceiveAsync(content, destination, progress, cancellationToken);
            }

            var to = $"→ {target}";
            var sent = $"{Bytes.Humanize(new FileInfo(staged).Length)}, sent in {ContainerTransfers.Spent(sending.Elapsed)}";
            bool replaces;
            using (Titled(id, "Upload", file, to, sent))
            {
                replaces = await TestAsync(id, "-e", ContainerPath.Join(target, file), cancellationToken);
            }

            using var titled = Titled(id, "Upload", file, to, replaces ? $"{sent} · replaces the one there" : sent);
            progress?.Copying();
            await CopyInAsync(id, staged, target, file, cancellationToken);
        }
        finally
        {
            Staging.Discard(staged);
        }

        return await StatAsync(id, ContainerPath.Join(target, file), cancellationToken);
    }

    /// <summary>
    /// The bytes of the request onto the staged file, counted as they land.
    /// Every half megabyte, not every buffer: often enough for a ring that
    /// moves on a slow link, rare enough not to repaint every row watching it
    /// a thousand times a second.
    /// </summary>
    private static async Task ReceiveAsync(Stream content, Stream destination, ITransferProgress? progress, CancellationToken cancellationToken)
    {
        var buffer = new byte[1 << 20];
        long received = 0;
        long reported = 0;
        while (await content.ReadAsync(buffer, cancellationToken) is var read && read > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            if (received - reported >= 512 * 1024)
            {
                reported = received;
                progress?.Progress(received);
            }
        }

        progress?.Progress(received);
    }

    /// <summary>Removes a file or a whole directory. The root is never removed.</summary>
    public async Task DeleteAsync(string container, string path, CancellationToken cancellationToken = default)
    {
        var id = WslcArgs.Require(container, "container");
        await Exec(id, ["rm", "-rf", "--", ContainerPath.File(path)], cancellationToken);
    }

    public async Task<ContainerFileEntry> MakeDirectoryAsync(string container, string path, CancellationToken cancellationToken = default)
    {
        var id = WslcArgs.Require(container, "container");
        var directory = ContainerPath.File(path);
        await Exec(id, ["mkdir", "-p", "--", directory], cancellationToken);
        return await StatAsync(id, directory, cancellationToken);
    }

    /// <summary>Renames or moves a path inside the container.</summary>
    public async Task<ContainerFileEntry> RenameAsync(string container, string source, string destination, CancellationToken cancellationToken = default)
    {
        var id = WslcArgs.Require(container, "container");
        var from = ContainerPath.File(source);
        var to = ContainerPath.File(destination);
        if (from != to)
        {
            await Exec(id, ["mv", "--", from, to], cancellationToken);
        }

        return await StatAsync(id, to, cancellationToken);
    }

    /// <summary>Copies a file or a whole directory inside the container.</summary>
    public async Task<ContainerFileEntry> CopyAsync(string container, string source, string destination, CancellationToken cancellationToken = default)
    {
        var id = WslcArgs.Require(container, "container");
        var from = ContainerPath.File(source);
        var to = ContainerPath.File(destination);
        if (from == to)
        {
            throw new ArgumentException("The source and the destination are the same path.", nameof(destination));
        }

        await Exec(id, ["cp", "-a", "--", from, to], cancellationToken);
        return await StatAsync(id, to, cancellationToken);
    }

    /// <summary>One entry, read back from its own directory, so an answer says what was really created.</summary>
    private async Task<ContainerFileEntry> StatAsync(string container, string path, CancellationToken cancellationToken)
    {
        var result = await Exec(container, ["ls", "-lad", "--", path], cancellationToken);
        return LsParsing.Entries(result.Stdout, ContainerPath.Parent(path)).FirstOrDefault()
            ?? new ContainerFileEntry(ContainerPath.Name(path), ContainerFileEntry.File, "", 0, "0 B", "", "", "", "", path);
    }

    private Task<WslcResult> Exec(string container, IReadOnlyList<string> command, CancellationToken cancellationToken) =>
        wslc.RunAsync(["exec", container, .. command], cancellationToken: cancellationToken);

    private async Task TryExec(string container, IReadOnlyList<string> command, CancellationToken cancellationToken)
    {
        try
        {
            await Exec(container, command, cancellationToken);
        }
        catch (WslcException ex)
        {
            logger.LogInformation("files: {Command} skipped: {Message}", string.Join(' ', command), ex.Message);
        }
    }

    private async Task<bool> TestAsync(string container, string flag, string path, CancellationToken cancellationToken)
    {
        try
        {
            await Exec(container, ["test", flag, path], cancellationToken);
            return true;
        }
        catch (WslcException ex) when (IsStopped(ex))
        {
            throw NotRunningError();
        }
        catch (WslcException)
        {
            return false;
        }
    }

    /// <summary>
    /// Why the path could not be used: a stopped container answers for
    /// everything, so it is reported first; then a path that is not there, and
    /// last one that is there but is not what the caller needed.
    /// </summary>
    private async Task<Exception> ExplainAsync(string container, string path, CancellationToken cancellationToken)
    {
        try
        {
            await Exec(container, ["test", "-e", path], cancellationToken);
            return new InvalidOperationException($"{path} is not a directory.");
        }
        catch (WslcException ex) when (IsStopped(ex))
        {
            return NotRunningError();
        }
        catch (WslcException)
        {
            return new InvalidOperationException($"{path} does not exist in this container.");
        }
    }

    private static InvalidOperationException NotRunningError() =>
        new("The container is not running. Start it to browse its files.");

    private static bool IsStopped(WslcException ex) =>
        $"{ex.Message} {ex.Result.Stderr}".Contains(NotRunning, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A name <c>wslc container cp</c> can carry. Its tar writes and reads the
    /// name of what it carries in the host's ANSI codepage, so anything that
    /// codepage has no letter for — <c>Ж</c>, <c>★</c>, an emoji — arrives
    /// empty and the copy ends in <c>E_FAIL</c>, whichever way it was going.
    /// Plain ASCII is what every codepage has, so a file that cannot travel
    /// under its own name travels under one of these and takes its own back
    /// inside the container, where there is only UTF-8.
    /// </summary>
    private static bool Travels(string name) => name.All(char.IsAscii);

    /// <summary>The plain name a file travels under when its own cannot be carried; it never survives the copy.</summary>
    private static string Travelling() => $"wslc-cp-{Guid.NewGuid():N}";

    /// <summary>
    /// What every command of one file's way in or out is called in CLI Activity
    /// and the log (<see cref="CliTitle"/>), in this order: the container by its name, which way, the file by its own name,
    /// what is known of it — its size, how long it took to send, that it
    /// replaces one — and the folder: "web · Upload · app.msi · 58.2 MiB, sent
    /// in 42.0 s → /data". Instead of the copy of a staged file under a
    /// temporary name, which is kept for the command itself, when the row opens.
    /// The time it happened is the row's own.
    /// </summary>
    private IDisposable Titled(string container, string verb, string name, string where, string? about = null) =>
        CliTitle.Use($"{registry.NameOf(ResourceRegistry.Container, container)} · {verb} · {name}{(about is null ? "" : $" · {about}")} {where}");

    /// <summary>
    /// The file under a name the copy can carry: a hard link beside it, so the
    /// bytes of what may be gigabytes are not written twice. <c>ln</c> reads
    /// through a symbolic link, which is what <c>--follow-link</c> is for
    /// below. A directory that cannot be written, or a filesystem with no
    /// links, falls back to a copy in <c>/tmp</c>. What it answers is the
    /// caller's to remove.
    /// </summary>
    private async Task<string> CarryableAsync(string container, string path, CancellationToken cancellationToken)
    {
        var plain = Travelling();
        var beside = ContainerPath.Join(ContainerPath.Parent(path), plain);
        try
        {
            await Exec(container, ["ln", "--", path, beside], cancellationToken);
            return beside;
        }
        catch (WslcException ex) when (!IsStopped(ex))
        {
            var copy = $"/tmp/{plain}";
            await Exec(container, ["cp", "-a", "--", path, copy], cancellationToken);
            return copy;
        }
    }

    /// <summary>
    /// <c>--follow-link</c> (wslc 2.9.13): what the user asked to download is the
    /// file they are looking at, not the twenty bytes of a symbolic link that
    /// points somewhere inside a container they cannot reach from here.
    /// </summary>
    private async Task CopyOutAsync(string container, string path, string staged, CancellationToken cancellationToken)
    {
        if (Travels(ContainerPath.Name(path)))
        {
            await wslc.RunAsync(["container", "cp", "--follow-link", $"{container}:{path}", staged], CopyTimeout, cancellationToken);
            return;
        }

        var carried = await CarryableAsync(container, path, cancellationToken);
        try
        {
            await wslc.RunAsync(["container", "cp", $"{container}:{carried}", staged], CopyTimeout, cancellationToken);
        }
        finally
        {
            await TryExec(container, ["rm", "-f", "--", carried], cancellationToken);
        }
    }

    /// <summary>
    /// The staged file into the container's directory, under
    /// <paramref name="name"/>. It lands under the name it travelled with —
    /// the staged one, which is the file name Windows accepted and the copy
    /// could carry — and is renamed there, where a name is only bytes.
    /// </summary>
    private async Task CopyInAsync(string container, string staged, string directory, string name, CancellationToken cancellationToken)
    {
        // wslc takes a directory as the destination and keeps the source's name.
        await wslc.RunAsync(["container", "cp", staged, $"{container}:{directory}"], CopyTimeout, cancellationToken);
        var landed = ContainerPath.Join(directory, Path.GetFileName(staged));
        var wanted = ContainerPath.Join(directory, name);
        if (landed == wanted)
        {
            return;
        }

        try
        {
            await Exec(container, ["mv", "-f", "--", landed, wanted], cancellationToken);
        }
        catch
        {
            // The name it travelled with is not a file the user asked for: it
            // does not stay behind in their directory.
            await TryExec(container, ["rm", "-f", "--", landed], cancellationToken);
            throw;
        }
    }
}
