using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.AgentLog;

/// <summary>
/// The agent's own log on disk, in this format
/// (<c>yyyy-MM-dd HH:mm:ss.fff | LEVEL    | source - message</c>, further lines
/// of a message below it). One file per process, so an agent restarting while
/// the old one still exits never shares a file; files of other processes older
/// than a week or over 20 MB are dropped when the agent starts. Lines are queued and written by one background writer that holds
/// <see cref="Gate"/>, which the Logs page's delete takes too.
/// </summary>
public sealed class AgentLogFile : IDisposable
{
    public const string FilePrefix = "wslc-ai-agent";
    private const long MaxBytes = 20L * 1024 * 1024;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _writer;

    public AgentLogFile(IOptions<WslcOptions> options)
    {
        Directory = options.Value.EffectiveLogDirectory;
        CurrentPath = Path.Combine(Directory, $"{FilePrefix}.{Environment.ProcessId}.log");
        System.IO.Directory.CreateDirectory(Directory);
        PruneOldFiles();
        _writer = Task.Run(WriteAsync);
    }

    public string Directory { get; }

    public string CurrentPath { get; }

    /// <summary>Held while lines are appended and while a delete rewrites the files.</summary>
    public Lock Gate { get; } = new();

    /// <summary>Every log file of the agent, current one included.</summary>
    public IReadOnlyList<string> Files() =>
        System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.GetFiles(Directory, $"{FilePrefix}*.log")
            : [];

    public void Append(string entry) => _lines.Writer.TryWrite(entry);

    private async Task WriteAsync()
    {
        var reader = _lines.Reader;
        while (await reader.WaitToReadAsync())
        {
            var batch = new StringBuilder();
            while (reader.TryRead(out var line))
            {
                batch.Append(line);
            }

            try
            {
                lock (Gate)
                {
                    using var stream = new FileStream(CurrentPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    stream.Write(Utf8.GetBytes(batch.ToString()));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A log line is not worth failing the agent for; the next batch tries again.
            }
        }
    }

    private void PruneOldFiles()
    {
        foreach (var path in Files().Where(path => !path.Equals(CurrentPath, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var file = new FileInfo(path);
                if (DateTime.UtcNow - file.LastWriteTimeUtc > MaxAge || file.Length > MaxBytes)
                {
                    file.Delete();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still open in an exiting agent, or not ours to delete: it waits for the next start.
            }
        }
    }

    public void Dispose()
    {
        _lines.Writer.TryComplete();
        _writer.Wait(TimeSpan.FromSeconds(2));
    }
}
