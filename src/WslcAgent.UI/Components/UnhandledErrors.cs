using Microsoft.Extensions.Logging;

namespace WslcAgent.UI.Components;

/// <summary>
/// The application's last catch. What nothing else caught — an exception in a
/// dialog, a provider, the layout, anything outside the page's own
/// <c>ErrorBoundary</c> — used to end in Blazor's own yellow bar at the foot
/// of the window, "An unhandled error has occurred", which should
/// never be seen: it looks final, and it is not — a WebAssembly app goes
/// on working after such an error. So the bar is gone from both <c>index.html</c>
/// files, and every error Blazor's renderer would have put there is reported
/// here instead, and the layout shows it as an error toast that stays and can
/// be copied. Both builds report through <see cref="UnhandledErrorLoggerProvider"/>:
/// the browser's renderer and the native web view's write them to the same
/// log, under their own categories, before they would show the bar.
/// </summary>
public sealed class UnhandledErrors
{
    /// <summary>An error nothing caught, with its message.</summary>
    public event Action<string>? Reported;

    public void Report(string message) => Reported?.Invoke(message);
}

/// <summary>
/// Blazor's renderer logs an error nothing caught, at Critical, under its own
/// category (<c>…Rendering.WebAssemblyRenderer</c> in the browser,
/// <c>…WebView.WebViewRenderer</c> in the native client) before it shows the
/// bar: this provider is that log's reader, and hands the error on.
/// </summary>
public sealed class UnhandledErrorLoggerProvider(UnhandledErrors errors) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        categoryName.StartsWith("Microsoft.AspNetCore.Components.", StringComparison.Ordinal) && categoryName.EndsWith("Renderer", StringComparison.Ordinal)
            ? new RendererLogger(errors)
            : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class RendererLogger(UnhandledErrors errors) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                errors.Report(exception?.Message ?? formatter(state, exception));
            }
        }
    }
}
