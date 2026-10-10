using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;

namespace WslcAgent.Server.Wslc;

/// <summary>
/// The last events the agent heard from <c>wslc events</c>, for
/// <c>GET /api/v1/events/recent</c>, the dashboard's Recent events and the
/// <c>recent_events</c> tool: until now the agent heard them only to tell the
/// screens to read again, and kept none. Held in memory, the last
/// <see cref="Kept"/>: the event store is the session's own and starts empty
/// with it, so a session the agent did not listen to before begins an empty
/// list, and a restarted agent does too.
/// </summary>
public sealed class RecentWslcEvents : IRecentEvents
{
    /// <summary>How many it keeps: a screenful many times over, and nothing a long-running agent would notice.</summary>
    public const int Kept = 100;

    private readonly LinkedList<WslcEventEntry> _events = [];
    private readonly Lock _gate = new();
    private string _session = "";

    /// <summary>One event heard, at the front.</summary>
    public void Heard(WslcEvent read)
    {
        lock (_gate)
        {
            _events.AddFirst(new WslcEventEntry(read.Time, read.Type, read.Action, read.Id, read.Name, read.ExitCode));
            if (_events.Count > Kept)
            {
                _events.RemoveLast();
            }
        }
    }

    /// <summary>
    /// The session a stream is about to be opened on: another one than the last
    /// starts the list over, since what happened in the other is not this one's.
    /// </summary>
    public void Listening(string session)
    {
        lock (_gate)
        {
            if (session != _session)
            {
                _events.Clear();
                _session = session;
            }
        }
    }

    /// <summary>The newest <paramref name="max"/> of them, newest first.</summary>
    public IReadOnlyList<WslcEventEntry> Recent(int max)
    {
        lock (_gate)
        {
            return [.. _events.Take(Math.Clamp(max, 1, Kept))];
        }
    }
}
