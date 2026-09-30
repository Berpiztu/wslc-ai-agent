using System.Threading.Channels;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Wslc;

/// <summary>
/// What the agent has learned from <c>wslc events</c>, handed to whoever is
/// listening: the clients over their WebSocket, and anything inside the agent
/// that wants to know. One notice names the kinds that changed, never the
/// objects — a screen reads its list again, which is where the data has always
/// come from.
/// </summary>
public sealed class WslcEvents
{
    /// <summary>A slow client is not allowed to hold the reader up; it is dropped and told to read everything again.</summary>
    private const int Backlog = 64;

    private readonly List<Channel<ChangeNotice>> _listeners = [];
    private readonly Lock _gate = new();

    /// <summary>True while a stream is open and the agent is being told what changes.</summary>
    public bool Live { get; internal set; }

    /// <summary>
    /// A listener's own queue, and the notices written to it until it is
    /// disposed. The first notice a listener gets is always a full one: it has
    /// missed everything that happened before it arrived.
    /// </summary>
    public Listener Listen()
    {
        var channel = Channel.CreateBounded<ChangeNotice>(new BoundedChannelOptions(Backlog)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        channel.Writer.TryWrite(ChangeNotice.Everything);
        lock (_gate)
        {
            _listeners.Add(channel);
        }

        return new Listener(this, channel);
    }

    /// <summary>Tells every listener; a full queue loses its oldest notice, which a later one covers.</summary>
    public void Publish(ChangeNotice notice)
    {
        lock (_gate)
        {
            foreach (var listener in _listeners)
            {
                listener.Writer.TryWrite(notice);
            }
        }
    }

    private void Forget(Channel<ChangeNotice> channel)
    {
        lock (_gate)
        {
            _listeners.Remove(channel);
        }

        channel.Writer.TryComplete();
    }

    /// <summary>One listener's queue; disposing it stops the agent writing to it.</summary>
    public sealed class Listener(WslcEvents events, Channel<ChangeNotice> channel) : IDisposable
    {
        public IAsyncEnumerable<ChangeNotice> ReadAllAsync(CancellationToken cancellationToken) =>
            channel.Reader.ReadAllAsync(cancellationToken);

        public void Dispose() => events.Forget(channel);
    }
}
