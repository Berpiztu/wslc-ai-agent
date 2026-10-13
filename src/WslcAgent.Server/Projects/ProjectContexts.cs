using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Projects;

/// <summary>
/// The build contexts sent to the agent: a Compose file read on another
/// machine has its <c>build</c> folders there, so the client sends each as a
/// tar archive and the agent builds from it, handing the archive to
/// <c>wslc build -</c> on its standard input (wslc 3.0.3 on). An archive is
/// never unpacked here: what is in it is the builder's to read.
/// <para>
/// Each is kept in the agent's data folder under a name made from its bytes.
/// The same folder sent again is then the same context: it need not be sent
/// twice, and a service built from it is the same service to a later save,
/// which compares what a container was made from. One not used for a week is
/// let go of when the next arrives; a build that asks for one that is gone
/// says so, and the client sends it again.
/// </para>
/// </summary>
public sealed partial class ProjectContexts(IOptions<WslcOptions> options)
{
    private const string Extension = ".tar";
    private static readonly TimeSpan KeptFor = TimeSpan.FromDays(7);

    private string Folder => Path.Combine(options.Value.DataDirectory, "project-contexts");

    /// <summary>Keeps what is sent, under the id its bytes give it; one already there is left as it is.</summary>
    public async Task<ProjectContext> SaveAsync(Stream archive, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Folder);
        Forget();

        // Written under another name first: the id is known only once every byte has been read.
        var arriving = Path.Combine(Folder, $"{Guid.NewGuid():N}.arriving");
        try
        {
            string id;
            long size;
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await using var file = new FileStream(arriving, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                var buffer = new byte[1 << 16];
                int read;
                while ((read = await archive.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                size = file.Length;
                id = IdOf(hash.GetHashAndReset());
            }

            if (size == 0)
            {
                throw new ArgumentException("The build context sent is empty.");
            }

            var kept = PathOf(id);
            if (File.Exists(kept))
            {
                File.SetLastWriteTimeUtc(kept, DateTime.UtcNow);
            }
            else
            {
                File.Move(arriving, kept);
            }

            return new ProjectContext(id, size);
        }
        finally
        {
            File.Delete(arriving);
        }
    }

    /// <summary>Whether a context is here, and its size.</summary>
    public ProjectContext Get(string id) =>
        Find(id) is { } path ? new ProjectContext(id, new FileInfo(path).Length) : new ProjectContext(id, 0, Present: false);

    /// <summary>
    /// The archive of a context, by its full path; null when it is not here,
    /// or the id is no id of ours (it is a part of a path, so only what an
    /// id can be is looked for). Asked for, it is in use: its week begins
    /// again.
    /// </summary>
    public string? Find(string id)
    {
        if (!Id().IsMatch(id))
        {
            return null;
        }

        var path = PathOf(id);
        if (!File.Exists(path))
        {
            return null;
        }

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        return path;
    }

    /// <summary>A path is an archive kept here: what a build reads from its standard input instead of taking as a folder.</summary>
    public static bool IsArchive(string context) =>
        context.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) && Id().IsMatch(Path.GetFileNameWithoutExtension(context));

    /// <summary>The id of an archive's bytes: the first half of their SHA-256, which a client works out the same way before it sends anything.</summary>
    public static string IdOf(byte[] sha256) => Convert.ToHexStringLower(sha256)[..32];

    private string PathOf(string id) => Path.Combine(Folder, id + Extension);

    /// <summary>Lets go of the contexts nobody has asked for in a week, and of what an upload cut short left behind.</summary>
    private void Forget()
    {
        var before = DateTime.UtcNow - KeptFor;
        foreach (var file in Directory.EnumerateFiles(Folder))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < before)
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use by a build: it is looked at again when the next context arrives.
            }
        }
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex Id();
}
