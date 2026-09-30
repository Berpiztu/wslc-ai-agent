using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.ApiClient;

/// <summary>
/// What the agent says has changed, for every screen at once: one WebSocket for
/// the whole application, opened when the first list asks and kept while any of
/// them is up. A screen listens here instead of asking the agent every few
/// seconds — and when this is not connected the screens fall back to their own
/// slow reading, so the application never depends on it being there. The tray
/// icon listens here too, for the notifications it shows.
/// <para>
/// In C# on purpose: a WebSocket from Blazor is <see cref="ClientWebSocket"/>,
/// and this repository's JavaScript is for the terminal, the charts, the map,
/// the browser pane and a handful of named one-liners — not for this.
/// </para>
/// </summary>
public sealed class AgentChanges(WslcAgentApi api) : IAsyncDisposable
{
    /// <summary>A stream that will not open is retried, without ever becoming a busy loop.</summary>
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly CancellationTokenSource _stop = new();
    private Task? _reader;

    /// <summary>Raised for every notice, on whatever thread read it: a screen marshals it itself.</summary>
    public event Action<ChangeNotice>? Changed;

    /// <summary>True while the agent is telling us what changes; false falls the screens back to their own clock.</summary>
    public bool Live { get; private set; }

    /// <summary>Raised when <see cref="Live"/> changed, so a screen can slow down or speed up its own reading.</summary>
    public event Action? LiveChanged;

    /// <summary>Opens the stream the first time a screen asks; later calls are the same one.</summary>
    public void Start() => _reader ??= RunAsync(_stop.Token);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ReadAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Nothing to say to the user: the screens read on their own
                // while this is down, which is what the fallback is for.
            }
            finally
            {
                SetLive(false);
            }

            try
            {
                await Task.Delay(Retry, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(api.ChangeStreamUrl(), cancellationToken);
        SetLive(true);

        var buffer = new byte[8 * 1024];
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var received = await socket.ReceiveAsync(buffer, cancellationToken);
            if (received.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            // One notice per message, and a notice is small: anything that does
            // not fit this buffer is not one, and is left alone.
            if (received.EndOfMessage &&
                JsonSerializer.Deserialize<ChangeNotice>(Encoding.UTF8.GetString(buffer, 0, received.Count), Json) is { } notice)
            {
                Changed?.Invoke(notice);
            }
        }
    }

    private void SetLive(bool live)
    {
        if (Live == live)
        {
            return;
        }

        Live = live;
        LiveChanged?.Invoke();
    }
}
