using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Projects;

/// <summary>
/// The build contexts sent to the agent: a Compose file read on another
/// machine has its <c>build</c> folders there, so the client sends what is
/// in them and the agent builds from it, handing it to <c>wslc build -</c> as
/// a tar archive on its standard input (wslc 3.0.3 on).
/// <para>
/// A context is sent as what it is made of, not as one piece. Every file is
/// kept once, under the hash of its bytes, whichever context and whichever
/// folder it came in; a context is the list of its entries, each file named
/// with its hash. So a client asks first which of its files the agent lacks
/// and sends those alone: one Dockerfile changed in a folder of fifty
/// megabytes travels as one Dockerfile. The archive a build reads is made
/// here from the list, when the build begins.
/// </para>
/// <para>
/// Nothing sent is ever laid out as the folder it was: a file is kept under
/// its hash and read back into the archive, whose names are the builder's to
/// make something of. A context is known by what it holds (the names and the
/// bytes, not the moment each file was last written), so the same folder
/// sent again is the same context to a later save, which compares what a
/// container was made from. One not used for a week is let go of when the
/// next arrives, unless a project the agent keeps was built from it, and
/// then the files no context names any more; a build that asks for one that
/// is gone says so, and the client sends it again.
/// </para>
/// </summary>
public sealed partial class ProjectContexts(IOptions<WslcOptions> options, ILogger<ProjectContexts>? logger = null, ProjectStore? projects = null)
{
    private const string ListExtension = ".json";
    private static readonly TimeSpan KeptFor = TimeSpan.FromDays(7);

    /// <summary>How long a file no context names is left alone: it may be one of a context still on its way, whose list has not arrived.</summary>
    private static readonly TimeSpan Unnamed = TimeSpan.FromDays(1);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The rights of every entry of the archive: <c>rwxr-xr-x</c>, what a
    /// build from Windows gives them, since Windows keeps none of its own.
    /// </summary>
    private const UnixFileMode Rights =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private string Folder => Path.Combine(options.Value.DataDirectory, "project-contexts");

    private string Files => Path.Combine(Folder, "files");

    /// <summary>Of the files a client has, by their hashes, the ones the agent lacks: what there is to send.</summary>
    public IReadOnlyList<string> Missing(IReadOnlyList<string> hashes) =>
        [.. hashes.Distinct(StringComparer.Ordinal).Where(hash => !File.Exists(FilePath(hash)))];

    /// <summary>
    /// Keeps the files sent, which come as a tar archive whose entries are
    /// named by the hash of what they hold. Each is hashed again as it is
    /// written, and one whose bytes are not the ones its name says is
    /// refused: a file that changed between being read and being sent would
    /// otherwise be kept as what it no longer is. Answers how many it kept.
    /// <paramref name="received"/> is told how many bytes of the archive have
    /// arrived so far, as they do: what the transfer's row counts.
    /// </summary>
    public async Task<int> SaveFilesAsync(Stream archive, Action<long>? received = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Files);
        var kept = 0;
        await using var counted = new Counted(archive, received);
        await using var reader = new TarReader(counted, leaveOpen: true);
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
            {
                continue;
            }

            var named = FilePath(entry.Name);

            // Written under another name first: it is this file only once every byte agrees.
            var arriving = Path.Combine(Files, $"{Guid.NewGuid():N}.arriving");
            try
            {
                string hash;
                using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    await using var file = new FileStream(arriving, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                    var data = entry.DataStream ?? Stream.Null;
                    var buffer = new byte[1 << 16];
                    int read;
                    while ((read = await data.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        sha.AppendData(buffer, 0, read);
                        await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }

                    hash = Convert.ToHexStringLower(sha.GetHashAndReset());
                }

                if (hash != entry.Name)
                {
                    throw new ArgumentException("A file of the build context changed while it was being sent: what arrived is not what was read. Send it again.");
                }

                if (!File.Exists(named))
                {
                    File.Move(arriving, named);
                }

                kept++;
            }
            finally
            {
                File.Delete(arriving);
            }
        }

        return kept;
    }

    /// <summary>
    /// Keeps a context: the list of its entries, every file of which has to
    /// be here already. Answers the id the request of a Compose file names it
    /// by, which is made from what it holds.
    /// </summary>
    public ProjectContext Save(ProjectContextRequest request)
    {
        var entries = request.Entries ?? [];
        if (entries.Count == 0)
        {
            throw new ArgumentException("The build context sent is empty.");
        }

        foreach (var entry in entries)
        {
            if (!IsName(entry.Name))
            {
                throw new ArgumentException($"Not a name inside a build context: {entry.Name}");
            }

            if (entry.Blob.Length > 0 && !File.Exists(FilePath(entry.Blob)))
            {
                throw new ArgumentException($"The file {entry.Name} of the build context was not sent: its bytes are not on the agent. Send the context again.");
            }
        }

        // What the client could not read is not in the context, and a build that needed it
        // fails later for a file that is not there: written down where that is looked into.
        if (request.Skipped is { Count: > 0 } skipped)
        {
            logger?.LogWarning("Compose: the build context {Context} of {Project} came without {Count} entries its client could not read: {Entries}",
                request.Context is { Length: > 0 } context ? context : ".", request.Project is { Length: > 0 } project ? project : "a project", skipped.Count, string.Join(", ", skipped));
        }

        // The list is written before anything is let go of: a file it names that no other list
        // does, and that has been here a while, is then one that is named.
        Directory.CreateDirectory(Folder);
        var id = ProjectContextRequest.IdOf(entries);
        // Its entries alone: what is kept is what a build reads.
        File.WriteAllText(PathOf(id), JsonSerializer.Serialize(new ProjectContextRequest(entries), Json));
        Forget();
        return new ProjectContext(id, entries.Sum(entry => entry.Size), entries.Count(entry => entry.Blob.Length > 0));
    }

    /// <summary>
    /// Where the list of a context is, or will be, by its id: its full path,
    /// whether it has arrived or not, so a file that names a context is read
    /// the same before the context is sent and after; empty for what is no
    /// id of ours (it is a part of a path, so only what an id can be is
    /// taken). One that is here and is asked for is in use: its week begins
    /// again.
    /// </summary>
    public string PathFor(string id)
    {
        if (!Id().IsMatch(id))
        {
            return "";
        }

        var path = PathOf(id);
        if (File.Exists(path))
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }

        return path;
    }

    /// <summary>A build's context is one that was sent (the path of its list here), and no folder: it is read by the build from its standard input.</summary>
    public static bool IsSent(string context) =>
        context.EndsWith(ListExtension, StringComparison.OrdinalIgnoreCase) && Id().IsMatch(Path.GetFileNameWithoutExtension(context));

    /// <summary>
    /// The context as the tar archive a build reads, made now from its list
    /// and the files it names, and gone when it is closed. It begins with an
    /// entry for the folder itself, which has a short name: what reads the
    /// archive tells it is one by its first entry (seen with wslc 3.0.3,
    /// which takes for a Dockerfile an archive that begins any other way),
    /// and a file with a very long name is written in two parts.
    /// </summary>
    public async Task<Stream> OpenAsync(string list, CancellationToken cancellationToken = default)
    {
        var context = File.Exists(list) ? JsonSerializer.Deserialize<ProjectContextRequest>(await File.ReadAllTextAsync(list, cancellationToken), Json) : null;
        if (context?.Entries is not { Count: > 0 } entries)
        {
            throw new InvalidOperationException(Gone);
        }

        var archive = new FileStream(Path.Combine(Folder, $"{Guid.NewGuid():N}.packing"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await using (var tar = new TarWriter(archive, TarEntryFormat.Gnu, leaveOpen: true))
            {
                await tar.WriteEntryAsync(new GnuTarEntry(TarEntryType.Directory, "./") { Mode = Rights, ModificationTime = DateTimeOffset.UnixEpoch }, cancellationToken);
                foreach (var entry in entries)
                {
                    var changed = DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(entry.Modified, 0, DateTimeOffset.MaxValue.ToUnixTimeSeconds()));
                    if (entry.Blob.Length == 0)
                    {
                        await tar.WriteEntryAsync(new GnuTarEntry(TarEntryType.Directory, entry.Name + "/") { Mode = Rights, ModificationTime = changed }, cancellationToken);
                        continue;
                    }

                    var file = FilePath(entry.Blob);
                    if (!File.Exists(file))
                    {
                        throw new InvalidOperationException(Gone);
                    }

                    await using var data = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
                    await tar.WriteEntryAsync(new GnuTarEntry(TarEntryType.RegularFile, entry.Name) { Mode = Rights, ModificationTime = changed, DataStream = data }, cancellationToken);
                }
            }

            archive.Position = 0;
            return archive;
        }
        catch
        {
            await archive.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// The archive as it is read, telling how much of it has been, every half
    /// megabyte and at its end: an archive is read a file at a time, and the
    /// one who waits counts bytes.
    /// </summary>
    private sealed class Counted(Stream inner, Action<long>? tell) : Stream
    {
        private const long Every = 512 * 1024;
        private long _read;
        private long _told;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Tell(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Tell(await inner.ReadAsync(buffer, cancellationToken));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Tell(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Tell(int count)
        {
            _read += count;
            if (tell is not null && (count == 0 || _read - _told >= Every))
            {
                _told = _read;
                tell(_read);
            }

            return count;
        }
    }

    private const string Gone = "The build context is no longer on the agent: it was let go of since it was sent. Run the build again from the machine that has the folder.";

    /// <summary>A name an entry may have: a path of the context, down from it, with forward slashes.</summary>
    private static bool IsName(string name) =>
        name.Length > 0 && !name.Contains('\\') && !name.Contains('\0')
        && name.Split('/').All(part => part.Length > 0 && part is not ("." or ".."));

    private string PathOf(string id) => Path.Combine(Folder, id + ListExtension);

    /// <summary>Where a file is kept, by its hash: a part of a path, so only what a hash can be is taken.</summary>
    private string FilePath(string hash) =>
        Hash().IsMatch(hash) ? Path.Combine(Files, hash) : throw new ArgumentException($"Not the hash of a file: {hash}");

    /// <summary>
    /// Lets go of the contexts nobody has asked for in a week, of what an
    /// upload or a build cut short left behind, and then of the files no
    /// context names any more. Not of the ones a kept project was built from
    /// (<see cref="ProjectStore.Contexts"/>): they stay for as long as the
    /// project does, so it can be built again from them, however long ago
    /// they were sent, when the machine that has the folder is not at hand.
    /// </summary>
    private void Forget()
    {
        var before = DateTime.UtcNow - KeptFor;
        var ofProjects = projects?.Contexts() ?? [];
        foreach (var file in Directory.EnumerateFiles(Folder))
        {
            Try(() =>
            {
                if (File.GetLastWriteTimeUtc(file) < before && !ofProjects.Contains(Path.GetFileNameWithoutExtension(file)))
                {
                    File.Delete(file);
                }
            });
        }

        if (!Directory.Exists(Files))
        {
            return;
        }

        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var list in Directory.EnumerateFiles(Folder, "*" + ListExtension))
        {
            Try(() => named.UnionWith((JsonSerializer.Deserialize<ProjectContextRequest>(File.ReadAllText(list), Json)?.Entries ?? []).Select(entry => entry.Blob)));
        }

        var unnamedBefore = DateTime.UtcNow - Unnamed;
        foreach (var file in Directory.EnumerateFiles(Files))
        {
            Try(() =>
            {
                if (!named.Contains(Path.GetFileName(file)) && File.GetLastWriteTimeUtc(file) < unnamedBefore)
                {
                    File.Delete(file);
                }
            });
        }
    }

    /// <summary>What cannot be done to one file now (it is in use by a build, it is half written) is looked at again when the next context arrives.</summary>
    private static void Try(Action act)
    {
        try
        {
            act();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex Id();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Hash();
}
