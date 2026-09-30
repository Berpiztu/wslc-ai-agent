using System.Net.Http;
using WslcAgent.ApiClient;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// One of the agent's lists, read for the dashboard's objects on an interval:
/// one read for all the objects that show it, for as long as one follows it,
/// instead of one per object. A read that fails keeps what the last one had.
/// </summary>
public abstract class SharedRead<T>(TimeSpan every) : IDisposable where T : class
{
    private CancellationTokenSource? _reading;
    private int _followers;

    /// <summary>What the last read that answered brought; null until one has.</summary>
    public T? Value { get; private set; }

    /// <summary>A read answered.</summary>
    public event Action? Changed;

    /// <summary>Reads while someone follows; the last one to let go stops it.</summary>
    public IDisposable Follow()
    {
        if (_followers++ == 0)
        {
            _reading = new CancellationTokenSource();
            _ = ReadLoopAsync(_reading.Token);
        }

        return new Following(this);
    }

    /// <summary>A read now, for a pick that has to name what it picked.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRead)
        {
            return;
        }

        try
        {
            // A read that answered nothing keeps what the last one had.
            if (await ReadAsync(cancellationToken) is { } value)
            {
                Value = value;
                Keep(value);
                Changed?.Invoke();
            }
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            // What was read stays as it was; the next read tries again.
        }
    }

    public void Dispose() => _reading?.Cancel();

    /// <summary>Whether asking now is right; a list that asking would change says no.</summary>
    protected virtual bool CanRead => true;

    protected abstract Task<T?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>What a read that answered brought, for a read that keeps more than the last one.</summary>
    protected virtual void Keep(T value)
    {
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(every);
        try
        {
            do
            {
                await RefreshAsync(cancellationToken);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            // Nobody follows any more.
        }
    }

    private void Unfollow()
    {
        if (--_followers == 0)
        {
            _reading?.Cancel();
            _reading = null;
        }
    }

    private sealed class Following(SharedRead<T> read) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (!_done)
            {
                _done = true;
                read.Unfollow();
            }
        }
    }
}
