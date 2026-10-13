using System.Formats.Tar;
using System.Security.Cryptography;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace Berpiztu.Wslc.Compose;

/// <summary>
/// The folders a file's services are built from, sent to an agent on another
/// machine, which builds from them with <c>wslc build -</c> (wslc 3.0.3 on).
/// It is what makes <c>build</c> and <c>up</c> work against an agent that
/// cannot see this machine's disk.
/// <para>
/// A folder is sent as <c>docker build</c> would send it, without what its
/// <c>.dockerignore</c> leaves out, and only as much of it as the agent
/// lacks. Every file is read and hashed here; the agent, which keeps each
/// file once under its hash, says which of them it does not have; those are
/// sent, and then the list of the folder's entries, each file named by its
/// hash, which is the context. A Dockerfile changed in a folder of fifty
/// megabytes travels as a Dockerfile; a folder that did not change sends
/// nothing but the questions.
/// </para>
/// </summary>
internal sealed class BuildContexts(WslcAgentApi api)
{
    /// <summary>How often a step that takes long says how far it has got, in milliseconds.</summary>
    private const int Every = 250;

    /// <summary>
    /// Sends a folder as a build context and answers the id the agent knows
    /// it by. Each of its steps takes long with a large folder, and each
    /// says how far it has got as it goes: a line that stands still reads as
    /// a command that hung.
    /// </summary>
    /// <param name="written">The context as the Compose file writes it, for what is said.</param>
    /// <param name="project">The project it is for: what the agent lists the sending under, among its transfers.</param>
    public async Task<string> SendAsync(string folder, string written, string project, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
        {
            throw new CommandFailed($"The build context {written} is not a folder here: {folder}");
        }

        var (entries, paths) = await ReadAsync(folder, written, cancellationToken);
        var files = entries.Count(entry => entry.Blob.Length > 0);
        var whole = $"{files} files, {Size(entries.Sum(entry => entry.Size))}";

        Terminal.Moving($"Build context {written} ({whole}): asking the agent what it lacks");
        var missing = await api.GetMissingContextFilesAsync([.. paths.Keys], cancellationToken);
        var sent = missing.Count > 0 ? await SendFilesAsync([.. missing.Select(hash => (hash, paths[hash]))], written, project, files, cancellationToken) : 0;

        var id = (await api.SendProjectContextAsync(new ProjectContextRequest(entries), cancellationToken)).Id;
        Terminal.Settled(missing.Count > 0
            ? $"Build context {written}: {missing.Count} of {whole} sent ({Size(sent)}); the agent had the rest"
            : $"Build context {written}: nothing to send, the agent has it all ({whole})");
        return id;
    }

    /// <summary>
    /// The folder's entries as the context lists them, every file with the
    /// hash of its bytes, and where the file of each hash is here (the first
    /// of them, when several hold the same bytes: it is sent once).
    /// </summary>
    private static async Task<(List<ProjectContextEntry> Entries, Dictionary<string, string> Paths)> ReadAsync(string folder, string written, CancellationToken cancellationToken)
    {
        var ignoreFile = Path.Combine(folder, DockerIgnore.FileName);
        var ignore = DockerIgnore.Parse(File.Exists(ignoreFile) ? await File.ReadAllTextAsync(ignoreFile, cancellationToken) : null);
        var entries = new List<ProjectContextEntry>();
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        var (files, bytes, told) = (0, 0L, 0L);
        foreach (var (path, name, isFolder) in Entries(folder, "", ignore))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var changed = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero).ToUnixTimeSeconds();
            if (isFolder)
            {
                entries.Add(new ProjectContextEntry(name, 0, changed, ""));
                continue;
            }

            await using var data = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true);
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(data, cancellationToken));
            entries.Add(new ProjectContextEntry(name, data.Length, changed, hash));
            paths.TryAdd(hash, path);
            files++;
            bytes += data.Length;
            if (Environment.TickCount64 - told >= Every)
            {
                told = Environment.TickCount64;
                Terminal.Moving($"Reading build context {written}: {files} files, {Size(bytes)}");
            }
        }

        return (entries, paths);
    }

    /// <summary>
    /// Sends the files the agent lacks, packed into one tar archive whose
    /// entries are named by their hashes, which is how the agent takes them
    /// and checks each is what it says. Packed into a temporary file first,
    /// so what is sent has a size and a percentage. Answers how many bytes
    /// went.
    /// </summary>
    private async Task<long> SendFilesAsync(IReadOnlyList<(string Hash, string Path)> missing, string written, string project, int files, CancellationToken cancellationToken)
    {
        var archive = Path.Combine(Path.GetTempPath(), $"wslc-compose-{Guid.NewGuid():N}.tar");
        try
        {
            await using (var file = File.Create(archive))
            {
                await using var tar = new TarWriter(file, TarEntryFormat.Gnu, leaveOpen: true);
                var (packed, told) = (0, 0L);
                foreach (var (hash, path) in missing)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await using var data = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true);
                    await tar.WriteEntryAsync(new GnuTarEntry(TarEntryType.RegularFile, hash) { DataStream = data }, cancellationToken);
                    packed++;
                    if (Environment.TickCount64 - told >= Every)
                    {
                        told = Environment.TickCount64;
                        Terminal.Moving($"Packing build context {written}: {packed} of {missing.Count} files to send, {Size(file.Position)}");
                    }
                }
            }

            var size = new FileInfo(archive).Length;
            var what = $"{missing.Count} of {files} files, {Size(size)}";

            // In a file or a pipe nothing can be said as it goes: said once before, so the wait has a name.
            if (!Terminal.IsLive)
            {
                Terminal.Step($"Sending build context {written} ({what})");
            }

            // All of it read is not all of it kept: the agent still writes it down and answers.
            await using var sent = new Followed(File.OpenRead(archive), read => Terminal.Moving(read < size
                ? $"Sending build context {written} ({what}): {read * 100 / size} %"
                : $"Sending build context {written} ({what}): all of it sent, waiting for the agent to keep it"));
            await api.SendContextFilesAsync(sent, project, written, missing.Count, cancellationToken);
            return size;
        }
        finally
        {
            File.Delete(archive);
        }
    }

    /// <summary>
    /// The folder's entries, each by its path here and its name in the
    /// context (forward slashes, from the folder), a folder before what is in
    /// it and in the order of their names, so the same folder is listed the
    /// same way every time. A folder that is a link to another (a junction,
    /// a symbolic link) is not gone into: it may lead back to where it is. A
    /// folder the <c>.dockerignore</c> leaves out is not gone into either,
    /// unless the file has an exception written for something inside it
    /// (<see cref="DockerIgnore.Enters"/>).
    /// </summary>
    private static IEnumerable<(string Path, string Name, bool IsFolder)> Entries(string folder, string under, DockerIgnore ignore)
    {
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos().OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            var name = under.Length > 0 ? $"{under}/{entry.Name}" : entry.Name;
            var left = ignore.Leaves(name);
            if (entry is DirectoryInfo)
            {
                if (entry.LinkTarget is not null || !ignore.Enters(name))
                {
                    continue;
                }

                if (!left)
                {
                    yield return (entry.FullName, name, true);
                }

                foreach (var inside in Entries(entry.FullName, name, ignore))
                {
                    yield return inside;
                }
            }
            else if (!left)
            {
                yield return (entry.FullName, name, false);
            }
        }
    }

    private static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0.0} KB",
        _ => $"{bytes} bytes",
    };

    /// <summary>
    /// A file as it is read, saying how much of it has been: what is read to
    /// be sent is what has been sent, near enough. Told four times a second
    /// at most, and at its end.
    /// </summary>
    private sealed class Followed(FileStream file, Action<long> read) : Stream
    {
        private long _told = Environment.TickCount64 - Every;

        public override bool CanRead => true;

        public override bool CanSeek => file.CanSeek;

        public override bool CanWrite => false;

        public override long Length => file.Length;

        public override long Position
        {
            get => file.Position;
            set => file.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) => Tell(file.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Tell(await file.ReadAsync(buffer, cancellationToken));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Tell(await file.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

        public override long Seek(long offset, SeekOrigin origin) => file.Seek(offset, origin);

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                file.Dispose();
            }

            base.Dispose(disposing);
        }

        private int Tell(int count)
        {
            var now = Environment.TickCount64;
            if (count == 0 || now - _told >= Every)
            {
                _told = now;
                read(file.Position);
            }

            return count;
        }
    }
}
