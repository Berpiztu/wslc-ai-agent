namespace Berpiztu.Wslc.Compose;

/// <summary>
/// Ctrl+C. It stops the command: what was being waited for is let go and the
/// run ends as interrupted. A command with something better to do with the
/// first one takes it (<see cref="TakeNext"/>): an <c>up</c> cancels the work
/// the agent is doing, and waits for the agent to say it stopped. The one
/// after that stops the command, whatever it was waiting for.
/// </summary>
internal sealed class Interrupt : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Action? _first;

    public Interrupt() => Console.CancelKeyPress += OnPressed;

    /// <summary>Cancelled when the command is to stop.</summary>
    public CancellationToken Token => _stop.Token;

    /// <summary>The next Ctrl+C is handed to <paramref name="first"/> instead of stopping the command.</summary>
    public void TakeNext(Action first) => _first = first;

    private void OnPressed(object? sender, ConsoleCancelEventArgs pressed)
    {
        // The process is not killed where it stands: the command ends by its own steps.
        pressed.Cancel = true;
        if (Interlocked.Exchange(ref _first, null) is { } first)
        {
            first();
        }
        else
        {
            _stop.Cancel();
        }
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= OnPressed;
        _stop.Dispose();
    }
}
