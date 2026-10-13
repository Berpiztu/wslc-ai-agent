using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using WslcAgent.ApiClient;

namespace Berpiztu.Wslc.Compose;

/// <summary>
/// The folders a file's services are built from, sent to an agent on another
/// machine: each packed here as a tar archive, which the agent keeps and
/// hands to <c>wslc build</c> on its standard input (wslc 3.0.3 on). It is
/// what makes <c>build</c> and <c>up</c> work against an agent that cannot
/// see this machine's disk.
/// <para>
/// The archive is the folder as <c>docker build</c> would send it: without
/// what its <c>.dockerignore</c> leaves out, every entry with the rights a
/// build from Windows gives it (read, write and run for its owner, read and
/// run for the rest), since Windows keeps none of its own. It is written the
/// same way every time, byte for byte, for as long as the folder does not
/// change: the agent knows a context by its bytes, so one it already has is
/// not sent again, and a service built from it is the same service to the
/// next <c>up</c>.
/// </para>
/// </summary>
internal sealed class BuildContexts(WslcAgentApi api)
{
    private const string IgnoreFile = ".dockerignore";
    private const string Dockerfile = "Dockerfile";

    /// <summary>The rights of every entry: <c>rwxr-xr-x</c>.</summary>
    private const UnixFileMode Rights =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    /// <summary>
    /// Sends a folder as a build context and answers the id the agent knows
    /// it by. Packed into a temporary file first: its id is made from all of
    /// its bytes, and the agent is asked whether it has it before any is sent.
    /// </summary>
    /// <param name="written">The context as the Compose file writes it, for what is said.</param>
    public async Task<string> SendAsync(string folder, string written, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
        {
            throw new CommandFailed($"The build context {written} is not a folder here: {folder}");
        }

        var archive = Path.Combine(Path.GetTempPath(), $"wslc-compose-{Guid.NewGuid():N}.tar");
        try
        {
            // Each of the three takes long with a folder of hundreds of megabytes, and each says
            // how far it has got as it goes: a line that stands still reads as a command that hung.
            var files = await PackAsync(folder, archive, (count, bytes) => Terminal.Moving($"Packing build context {written}: {count} files, {Size(bytes)}"), cancellationToken);
            var size = new FileInfo(archive).Length;
            var what = $"{Size(size)}, {files} files";
            string id;
            await using (var packed = new Followed(File.OpenRead(archive), read => Terminal.Moving($"Checking build context {written} ({what}): {read * 100 / size} %")))
            {
                // The agent's own way of naming it (ProjectContexts.IdOf): the first half of the SHA-256.
                id = Convert.ToHexStringLower(await SHA256.HashDataAsync(packed, cancellationToken))[..32];
            }

            if ((await api.GetProjectContextAsync(id, cancellationToken)).Present)
            {
                Terminal.Settled($"Build context {written}: the agent has it already ({what})");
                return id;
            }

            // In a file or a pipe nothing can be said as it goes: said once before, so the wait has a name.
            if (!Terminal.IsLive)
            {
                Terminal.Step($"Sending build context {written} ({what})");
            }

            // All of it read is not all of it kept: the agent still writes it down and answers.
            await using var sent = new Followed(File.OpenRead(archive), read => Terminal.Moving(read < size
                ? $"Sending build context {written}: {read * 100 / size} % ({Size(read)} of {Size(size)})"
                : $"Sending build context {written}: all of it sent ({Size(size)}), waiting for the agent to keep it"));
            var kept = (await api.SendProjectContextAsync(sent, cancellationToken)).Id;
            Terminal.Settled($"Build context {written} sent ({what})");
            return kept;
        }
        finally
        {
            File.Delete(archive);
        }
    }

    /// <summary>
    /// A file as it is read, saying how much of it has been: what is read to
    /// be sent is what has been sent, near enough, and what is read to be
    /// checked is what has been checked. Told four times a second at most,
    /// and at its end.
    /// </summary>
    private sealed class Followed(FileStream file, Action<long> read) : Stream
    {
        private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(250);
        private long _told = Environment.TickCount64 - (long)Every.TotalMilliseconds;

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
            if (count == 0 || now - _told >= Every.TotalMilliseconds)
            {
                _told = now;
                read(file.Position);
            }

            return count;
        }
    }

    /// <summary>
    /// Packs the folder, in the order of its entries' names, and answers how
    /// many files went in. The archive begins with an entry for the folder
    /// itself, which has a short name: what reads the archive tells it is
    /// one by its first entry, and a file with a very long name, which this
    /// format writes in two parts, would not be taken for one.
    /// </summary>
    /// <param name="packed">Told as it goes, four times a second at most: how many files are in, and how many bytes the archive has.</param>
    private static async Task<int> PackAsync(string folder, string archive, Action<int, long> packed, CancellationToken cancellationToken)
    {
        var ignore = Ignore.Read(folder);
        await using var file = File.Create(archive);
        await using var tar = new TarWriter(file, TarEntryFormat.Gnu, leaveOpen: false);
        await tar.WriteEntryAsync(new GnuTarEntry(TarEntryType.Directory, "./") { Mode = Rights, ModificationTime = DateTimeOffset.UnixEpoch }, cancellationToken);
        var files = 0;
        var told = 0L;
        foreach (var (path, name, isFolder) in Entries(folder, "", ignore))
        {
            if (Environment.TickCount64 - told >= 250)
            {
                told = Environment.TickCount64;
                packed(files, file.Position);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // To the second: the format keeps no more, and the same moment has to be written the same way.
            var changed = DateTimeOffset.FromUnixTimeSeconds(new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero).ToUnixTimeSeconds());
            if (isFolder)
            {
                await tar.WriteEntryAsync(new GnuTarEntry(TarEntryType.Directory, name + "/") { Mode = Rights, ModificationTime = changed }, cancellationToken);
                continue;
            }

            await using var data = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true);
            await tar.WriteEntryAsync(new GnuTarEntry(TarEntryType.RegularFile, name) { Mode = Rights, ModificationTime = changed, DataStream = data }, cancellationToken);
            files++;
        }

        return files;
    }

    /// <summary>
    /// The folder's entries, each by its path here and its name in the
    /// archive (forward slashes, from the folder), a folder before what is in
    /// it. A folder that is a link to another (a junction, a symbolic link)
    /// is not gone into: it may lead back to where it is. A folder the
    /// <c>.dockerignore</c> leaves out is not gone into either, unless the
    /// file has an exception that could bring back something inside it.
    /// </summary>
    private static IEnumerable<(string Path, string Name, bool IsFolder)> Entries(string folder, string under, Ignore ignore)
    {
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos().OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            var name = under.Length > 0 ? $"{under}/{entry.Name}" : entry.Name;
            var left = ignore.Leaves(name);
            if (entry is DirectoryInfo)
            {
                if (entry.LinkTarget is not null || (left && !ignore.HasExceptions))
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
    /// A folder's <c>.dockerignore</c>: what is not sent. A pattern a line,
    /// from the folder: <c>*</c> for any run of characters of one name,
    /// <c>?</c> for one, <c>[abc]</c> for one of those, <c>**</c> for any
    /// number of folders; a pattern that
    /// names a folder leaves out what is in it; <c>!</c> before one brings
    /// back what an earlier one left out; the last pattern that matches
    /// decides. The Dockerfile and the <c>.dockerignore</c> at the folder's
    /// top are sent whatever it says, as <c>docker build</c> sends them.
    /// </summary>
    private sealed class Ignore
    {
        private readonly List<(Regex Pattern, bool Keeps)> _rules = [];

        /// <summary>Some pattern brings something back: a folder left out may still hold what is sent.</summary>
        public bool HasExceptions => _rules.Any(rule => rule.Keeps);

        public static Ignore Read(string folder)
        {
            var ignore = new Ignore();
            var file = Path.Combine(folder, IgnoreFile);
            if (!File.Exists(file))
            {
                return ignore;
            }

            foreach (var written in File.ReadAllLines(file))
            {
                var line = written.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var keeps = line.StartsWith('!');
                var pattern = (keeps ? line[1..].Trim() : line).Replace('\\', '/').Trim('/');
                while (pattern.StartsWith("./", StringComparison.Ordinal))
                {
                    pattern = pattern[2..];
                }

                if (pattern.Length > 0)
                {
                    ignore._rules.Add((Compiled(pattern), keeps));
                }
            }

            return ignore;
        }

        /// <summary>The entry is left out of what is sent.</summary>
        public bool Leaves(string name)
        {
            if (name is Dockerfile or IgnoreFile)
            {
                return false;
            }

            var left = false;
            foreach (var (pattern, keeps) in _rules)
            {
                if (pattern.IsMatch(name))
                {
                    left = !keeps;
                }
            }

            return left;
        }

        /// <summary>A pattern as the expression that matches the entry it names and everything under it.</summary>
        private static Regex Compiled(string pattern)
        {
            var expression = new StringBuilder("^");
            for (var at = 0; at < pattern.Length; at++)
            {
                if (pattern[at] == '*' && at + 1 < pattern.Length && pattern[at + 1] == '*')
                {
                    // Any number of folders: with the slash after it, none at all too.
                    var folders = at + 2 < pattern.Length && pattern[at + 2] == '/';
                    expression.Append(folders ? "(?:.*/)?" : ".*");
                    at += folders ? 2 : 1;
                }
                else if (Class(pattern, at) is { } named)
                {
                    expression.Append(named.Expression);
                    at = named.End;
                }
                else
                {
                    expression.Append(pattern[at] switch
                    {
                        '*' => "[^/]*",
                        '?' => "[^/]",
                        var other => Regex.Escape(other.ToString()),
                    });
                }
            }

            return new Regex(expression.Append("(?:/.*)?$").ToString(), RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// The class of characters a pattern opens at <paramref name="at"/>
        /// (<c>*.py[cod]</c>: one of those; with <c>!</c> or <c>^</c> first,
        /// one of none of them), as an expression, and where it closes. Null
        /// for a bracket that opens none (it is not closed, or holds nothing):
        /// it is then the character it is.
        /// </summary>
        private static (string Expression, int End)? Class(string pattern, int at)
        {
            if (pattern[at] != '[' || at + 2 >= pattern.Length)
            {
                return null;
            }

            var close = pattern.IndexOf(']', at + 2);
            if (close < 0)
            {
                return null;
            }

            var inside = pattern[(at + 1)..close];
            var none = inside[0] is '!' or '^';
            var named = none ? inside[1..] : inside;
            return named.Length == 0 ? null : ($"[{(none ? "^" : "")}{named.Replace("\\", "\\\\")}]", close);
        }
    }
}
