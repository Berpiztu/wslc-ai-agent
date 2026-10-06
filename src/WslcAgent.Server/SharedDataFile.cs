namespace WslcAgent.Server;

/// <summary>
/// A file in the agent's data folder that is not one process's alone: an
/// installed agent and a development one on the same machine share the
/// folder, and each that kept the copy it read when it started, and wrote
/// it back whole, took away what the other had written. So a change is made
/// under a lock on the file that every process takes (<see cref="Lock"/>), on
/// the file as it is now — a process that keeps what it read takes it again
/// once another has written it since (<see cref="WrittenSinceRead"/>) — and the
/// file is written whole, beside itself and moved into place, so a read
/// without the lock never finds it half-written.
/// </summary>
public sealed class SharedDataFile(string path, ILogger logger)
{
    /// <summary>How long a change waits for another process to let go of the file before it goes on without the lock.</summary>
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(3);

    private readonly string _lockPath = path + ".lock";

    /// <summary>When the file was written as this process last read or wrote it.</summary>
    private DateTime _readWritten;

    private string Name => Path.GetFileName(path);

    /// <summary>Another process has written the file since this one last read or wrote it.</summary>
    public bool WrittenSinceRead => Written() != _readWritten;

    /// <summary>
    /// The file's text; null when there is none. Throws what reading throws,
    /// and then the file still counts as unread, so the next look tries again.
    /// </summary>
    public string? Read()
    {
        var written = Written();
        var text = File.Exists(path) ? File.ReadAllText(path) : null;
        _readWritten = written;
        return text;
    }

    /// <summary>
    /// The text written beside the file and put in its place whole. A process
    /// reading the file at that moment holds it for as long as a read takes,
    /// so the move is tried again for a while before it gives up.
    /// </summary>
    public void Write(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var written = path + ".tmp";
        File.WriteAllText(written, text);
        var deadline = DateTime.UtcNow + LockWait;
        while (true)
        {
            try
            {
                File.Move(written, path, overwrite: true);
                break;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
            }
        }

        _readWritten = Written();
    }

    /// <summary>The file gone, for every process.</summary>
    public void Delete()
    {
        File.Delete(path);
        _readWritten = Written();
    }

    /// <summary>
    /// The lock every process takes to change the file: a file beside it opened
    /// by one alone, held until disposed. Null when another process held it for
    /// longer than <see cref="LockWait"/>: the change goes on without it.
    /// </summary>
    public IDisposable? Lock()
    {
        var deadline = DateTime.UtcNow + LockWait;
        while (true)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_lockPath)!);
                return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("{File} could not be locked: {Message}", Name, exception.Message);
                return null;
            }
        }
    }

    private DateTime Written()
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return _readWritten;
        }
    }
}
