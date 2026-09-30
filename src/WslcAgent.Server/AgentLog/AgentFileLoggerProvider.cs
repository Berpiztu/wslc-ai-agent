using System.Globalization;
using System.Text;

namespace WslcAgent.Server.AgentLog;

/// <summary>Sends every log entry the agent writes to <see cref="AgentLogFile"/>.</summary>
[ProviderAlias("AgentFile")]
public sealed class AgentFileLoggerProvider(AgentLogFile file) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new FileLogger(file, categoryName);

    public void Dispose()
    {
        // The file belongs to the container, which disposes it after the last logger.
    }

    /// <summary><c>Information</c> → <c>INFO</c>: the names the Logs page filters by.</summary>
    internal static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace or LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARNING",
        LogLevel.Error => "ERROR",
        _ => "CRITICAL",
    };

    internal static string Format(DateTime local, LogLevel level, string source, string message, Exception? exception)
    {
        var entry = new StringBuilder()
            .Append(local.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(" | ").Append(LevelName(level).PadRight(8))
            .Append(" | ").Append(source)
            .Append(" - ").Append(message.ReplaceLineEndings("\n"));
        if (exception is not null)
        {
            entry.Append('\n').Append(exception.ToString().ReplaceLineEndings("\n"));
        }

        return entry.Append('\n').ToString();
    }

    private sealed class FileLogger(AgentLogFile file, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                file.Append(Format(DateTime.Now, logLevel, category, formatter(state, exception), exception));
            }
        }
    }
}
