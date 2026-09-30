using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Notifications;

/// <summary>
/// A container that stopped without being asked to, heard from
/// <c>wslc events</c>. A stop someone asked
/// for — from the application, the MCP tools or a terminal, a restart or a
/// recreate alike — is killed first: <c>kill</c>, then its <c>stop</c> with the
/// exit code. A <c>stop</c> with no <c>kill</c> of the same container before it
/// is one nobody asked for. A session stopped takes its containers down
/// without a word on the stream, so those never reach here; the session's own
/// notification says it.
/// </summary>
public sealed class ContainerStops(Notifier notifier, IContainerService containers, TimeProvider time, ILogger<ContainerStops> logger)
{
    /// <summary>A stop asked for is killed first, and a kill that is not answered by its stop after ten seconds is killed again: a minute covers both.</summary>
    private static readonly TimeSpan Asked = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, DateTimeOffset> _killed = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>One event, as the reader reads it: a kill is remembered, a stop with none before it notifies.</summary>
    public void Heard(WslcEvent heard)
    {
        if (heard.Type != "container")
        {
            return;
        }

        var now = time.GetUtcNow();
        lock (_gate)
        {
            foreach (var old in _killed.Where(k => now - k.Value > Asked).Select(k => k.Key).ToList())
            {
                _killed.Remove(old);
            }

            if (heard.Action == "kill")
            {
                _killed[heard.Id] = now;
                return;
            }

            if (heard.Action != "stop" || _killed.Remove(heard.Id))
            {
                return;
            }
        }

        _ = NotifyAsync(heard);
    }

    /// <summary>The notification, under the container's name, which the event's bracket holds but cannot be read from honestly: the list has it.</summary>
    private async Task NotifyAsync(WslcEvent stopped)
    {
        var name = stopped.Id.Length > 12 ? stopped.Id[..12] : stopped.Id;
        try
        {
            var list = await containers.ListAsync(all: true);
            if (list.Containers.FirstOrDefault(c => c.Id.Length > 0 && (stopped.Id.StartsWith(c.Id, StringComparison.Ordinal) || c.Id.StartsWith(stopped.Id, StringComparison.Ordinal))) is { } found)
            {
                name = found.Name;
            }
        }
        catch (Exception failed) when (failed is WslcException or WslcNotFoundException or TimeoutException or InvalidOperationException)
        {
            logger.LogDebug("notifications: the stopped container's name could not be read: {Message}", failed.Message);
        }

        var code = stopped.ExitCode is { } exit ? $", exit code {exit}" : "";
        notifier.Raise(NotificationKind.ContainerStopped, NotificationSeverity.Error,
            $"{name} stopped", $"The container stopped without being asked to{code}.", NotificationLink.Containers);
    }
}
