using System.Text.Json;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.ApiClient;

/// <summary>
/// The agent's notifications as they arrive, for whoever shows them on
/// Windows: the agent's tray icon on its own machine, the Windows client on
/// another. It listens on the events stream and,
/// on each <c>notification</c> — and on each connection, whose first notice is
/// a full one — reads those after the last it handed over. The last id is
/// kept by the caller, so an icon or a client started again, or an agent that
/// updated itself meanwhile, shows what was missed and nothing twice; one that
/// never saw one starts from the last raised, not from the whole history.
/// </summary>
public sealed class NotificationFeed : IDisposable
{
    /// <summary>At most this many from one read; the rest are counted, not shown.</summary>
    public const int MostAtOnce = 5;

    private readonly WslcAgentApi _api;
    private readonly AgentChanges _changes;
    private readonly Func<long?> _last;
    private readonly Action<long> _remember;
    private readonly Action<IReadOnlyList<AgentNotification>, int> _arrived;
    private readonly SemaphoreSlim _reading = new(1, 1);

    /// <param name="api">The agent.</param>
    /// <param name="changes">Its events stream, started here if nothing started it.</param>
    /// <param name="last">The last id handed over, as kept; null when none ever was.</param>
    /// <param name="remember">Keeps the last id handed over.</param>
    /// <param name="arrived">The newest ones, oldest first, at most <see cref="MostAtOnce"/>, and how many older were left out.</param>
    public NotificationFeed(WslcAgentApi api, AgentChanges changes, Func<long?> last, Action<long> remember, Action<IReadOnlyList<AgentNotification>, int> arrived)
    {
        _api = api;
        _changes = changes;
        _last = last;
        _remember = remember;
        _arrived = arrived;
        _changes.Changed += OnChanged;
        _changes.Start();
    }

    private void OnChanged(ChangeNotice notice)
    {
        if (notice.Touches(ChangeNotice.Notification))
        {
            _ = ReadAsync();
        }
    }

    private async Task ReadAsync()
    {
        await _reading.WaitAsync();
        try
        {
            var seen = _last();
            var list = await _api.GetNotificationsAsync(seen ?? long.MaxValue);
            if (seen is not { } last || list.Latest < last)
            {
                // Never shown one, or the agent's list started again (its
                // data folder emptied): from now on, not the whole history.
                _remember(list.Latest);
                return;
            }

            if (list.Notifications.Count == 0)
            {
                return;
            }

            _remember(list.Latest);
            _arrived([.. list.Notifications.TakeLast(MostAtOnce)], Math.Max(0, list.Notifications.Count - MostAtOnce));
        }
        catch (Exception failed) when (failed is HttpRequestException or AgentApiException or TaskCanceledException or JsonException)
        {
            // The agent is away; the stream says when it is back, and this is read again.
        }
        finally
        {
            _reading.Release();
        }
    }

    /// <summary>Stops listening; a read under way ends on its own.</summary>
    public void Dispose() => _changes.Changed -= OnChanged;
}
