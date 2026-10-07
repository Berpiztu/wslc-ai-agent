using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Notifications;

/// <summary>
/// A container that stopped without being asked to, heard from
/// <c>wslc events</c>. A stop someone asked for — from the application, the
/// MCP tools or a terminal, a restart or a recreate alike — is killed first.
/// Up to wslc 3.0.1 the CLI answered <c>kill</c>, then <c>stop</c> with the
/// exit code; from 3.0.2 it reports as Docker does: <c>kill</c>, <c>die</c>
/// with the exit code, then <c>stop</c>, and a container that ends on its own
/// only <c>die</c>. So a <c>die</c> or a <c>stop</c> with no <c>kill</c> of the
/// same container before it is one nobody asked for, said once whichever
/// comes. A session stopped takes its containers down without a word on the
/// stream, so those never reach here; the session's own notification says it.
/// </summary>
public sealed class ContainerStops(Notifier notifier, ContainerNames names, TimeProvider time)
{
    /// <summary>A stop asked for is killed first, and a kill that is not answered by its stop after ten seconds is killed again: a minute covers both.</summary>
    private static readonly TimeSpan Asked = TimeSpan.FromMinutes(1);

    /// <summary>The containers killed: their end, when it comes, was asked for.</summary>
    private readonly Dictionary<string, DateTimeOffset> _killed = new(StringComparer.Ordinal);

    /// <summary>The containers whose <c>die</c> was told: the <c>stop</c> that may follow it is the same end.</summary>
    private readonly Dictionary<string, DateTimeOffset> _told = new(StringComparer.Ordinal);

    private readonly Lock _gate = new();

    /// <summary>One event, as the reader reads it: a kill is remembered, an end with none before it notifies.</summary>
    public void Heard(WslcEvent heard)
    {
        if (heard.Type != "container" || heard.Action is not ("kill" or "die" or "stop"))
        {
            return;
        }

        var now = time.GetUtcNow();
        lock (_gate)
        {
            Forget(_killed, now);
            Forget(_told, now);
            if (heard.Action == "kill")
            {
                _killed[heard.Id] = now;
                return;
            }

            if (_killed.ContainsKey(heard.Id))
            {
                // Asked for. Its die keeps the kill for the stop that follows
                // it; the stop is the last word of the end, and lets it go.
                if (heard.Action == "stop")
                {
                    _killed.Remove(heard.Id);
                }

                return;
            }

            if (heard.Action == "stop" && _told.Remove(heard.Id))
            {
                return;
            }

            if (heard.Action == "die")
            {
                _told[heard.Id] = now;
            }
        }

        _ = NotifyAsync(heard);
    }

    private static void Forget(Dictionary<string, DateTimeOffset> heard, DateTimeOffset now)
    {
        foreach (var old in heard.Where(h => now - h.Value > Asked).Select(h => h.Key).ToList())
        {
            heard.Remove(old);
        }
    }

    private async Task NotifyAsync(WslcEvent stopped)
    {
        var name = await names.OfAsync(stopped.Id);
        var code = stopped.ExitCode is { } exit ? $", exit code {exit}" : "";
        notifier.Raise(NotificationKind.ContainerStopped, NotificationSeverity.Error,
            $"{name} stopped", $"The container stopped without being asked to{code}.", NotificationLink.Containers);
    }
}
