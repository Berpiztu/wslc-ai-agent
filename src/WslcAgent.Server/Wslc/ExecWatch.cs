using System.Collections.Concurrent;
using System.Text.Json;

namespace WslcAgent.Server.Wslc;

/// <summary>
/// The commands run inside containers from outside the agent (a
/// <c>wslc exec</c> in a terminal, a script), for the Recent events list: one
/// entry, <c>exec</c>, when the command ends, with its exit code. Never its
/// command, which may carry a password; never a health check's probes, three
/// events every interval of every container that has one; never the agent's
/// own (<see cref="AgentExecs"/>). A probe is known by its command: the
/// container's <c>Healthcheck.Test</c>, which the exec's command ends with
/// (<c>CMD-SHELL</c> runs it behind the shell, <c>/bin/sh -c</c>).
/// </summary>
public sealed class ExecWatch(IWslcRunner wslc, AgentExecs agent, RecentWslcEvents recent, ILogger<ExecWatch> logger)
{
    /// <summary>The action an exec run from elsewhere is listed as.</summary>
    public const string Action = "exec";

    /// <summary>More execs created and never ended than this is a stream that lost their ends: the oldest go.</summary>
    private const int Pending = 500;

    /// <summary>Execs from elsewhere that have not ended yet, by their id, oldest first.</summary>
    private readonly Dictionary<string, ExecEvent> _created = [];
    private readonly Queue<string> _order = new();

    /// <summary>Each container's probe command, empty when it has none; read once per container, whose settings never change.</summary>
    private readonly ConcurrentDictionary<string, Task<string?>> _probes = new(StringComparer.Ordinal);

    /// <summary>One exec event, from the event reader's single thread; the task ends when what it brings is listed.</summary>
    public Task Heard(ExecEvent heard)
    {
        if (heard.Action == ExecEvent.Create)
        {
            if (!agent.Started(heard.ContainerId, heard.Name, heard.Time))
            {
                Remember(heard);
                _ = ProbeOf(heard.ContainerId);
            }

            return Task.CompletedTask;
        }

        return heard.Action == ExecEvent.Die && _created.Remove(heard.ExecId, out var created)
            ? ListAsync(created, heard)
            : Task.CompletedTask;
    }

    /// <summary>A new stream: the execs the last one saw begin will not be seen to end.</summary>
    public void Reset()
    {
        _created.Clear();
        _order.Clear();
    }

    private void Remember(ExecEvent created)
    {
        _created[created.ExecId] = created;
        _order.Enqueue(created.ExecId);
        while (_order.Count > Pending)
        {
            _created.Remove(_order.Dequeue());
        }
    }

    private async Task ListAsync(ExecEvent created, ExecEvent died)
    {
        var probe = await ProbeOf(created.ContainerId);
        if (probe is null || (probe.Length > 0 && created.Command.EndsWith(probe, StringComparison.Ordinal)))
        {
            return;
        }

        recent.Heard(new WslcEvent(died.Time, "container", Action, died.ContainerId, died.ExitCode, died.Name ?? created.Name));
    }

    /// <summary>The container's probe command; null when it could not be read, and asked again next time.</summary>
    private Task<string?> ProbeOf(string containerId)
    {
        var read = _probes.GetOrAdd(containerId, ReadProbeAsync);
        if (read.IsCompleted && read.Result is null)
        {
            _probes.TryRemove(new KeyValuePair<string, Task<string?>>(containerId, read));
        }

        return read;
    }

    private async Task<string?> ReadProbeAsync(string containerId)
    {
        try
        {
            var result = await wslc.RunAsync(["container", "inspect", containerId]);
            var root = WslcJson.ParseRows(result.Stdout).FirstOrDefault();
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("Config", out var config) || config.ValueKind != JsonValueKind.Object
                || !config.TryGetProperty("Healthcheck", out var health) || health.ValueKind != JsonValueKind.Object
                || !health.TryGetProperty("Test", out var test) || test.ValueKind != JsonValueKind.Array)
            {
                return "";
            }

            var words = test.EnumerateArray().Select(word => word.GetString() ?? "").ToList();
            return words is ["CMD" or "CMD-SHELL", _, ..] ? string.Join(' ', words.Skip(1)) : "";
        }
        catch (Exception failed) when (failed is WslcException or WslcNotFoundException or TimeoutException or JsonException)
        {
            // Not known is not "no probe": an exec of a container whose probe
            // could not be read is left out rather than listed as the user's.
            logger.LogDebug("exec events: the health check of {Container} could not be read: {Message}", containerId, failed.Message);
            _probes.TryRemove(containerId, out _);
            return null;
        }
    }
}
