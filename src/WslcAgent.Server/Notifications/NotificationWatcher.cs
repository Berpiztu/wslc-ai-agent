using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Notifications;

/// <summary>
/// The readings Settings › Notifications watches,
/// read by the agent itself on its own beat, whether a client is open or not:
/// the drive always, and the host's and the containers' CPU and memory while
/// the session runs — known by the events stream being open, which asks
/// nothing of <c>wslc</c>, since a command naming a stopped session opens it
/// again. A reading notifies once when it has stayed past its threshold for
/// its minutes, and once more, if asked, when it is back under it; never on
/// every read while it stays up.
/// </summary>
public sealed class NotificationWatcher(
    NotificationSettingsStore settings,
    Notifier notifier,
    IHomeService home,
    IContainerService containers,
    WslcEvents events,
    TimeProvider time,
    ILogger<NotificationWatcher> logger) : BackgroundService
{
    /// <summary>
    /// A screen's five seconds: at thirty, a
    /// container's burst at start-up, which the dashboard's dial showed at
    /// 100 %, fell between two reads and a threshold of 0 minutes never
    /// notified. With a screen open the runner answers from the read it just
    /// made; with none, it costs two lines in CLI Activity every beat.
    /// </summary>
    private static readonly TimeSpan Beat = TimeSpan.FromSeconds(5);

    /// <summary>Each reading watched, by its key (<c>host-cpu</c>, <c>container-memory:&lt;id&gt;</c>): since when it is past its threshold, and whether that was said.</summary>
    private readonly Dictionary<string, Watch> _watches = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Beat, time);
        try
        {
            do
            {
                try
                {
                    await LookAsync(stoppingToken);
                }
                catch (Exception failed) when (failed is not OperationCanceledException)
                {
                    // A read that fails is a missed beat, never the agent down:
                    // a background service that throws takes the host with it.
                    logger.LogDebug("notifications: a reading failed: {Message}", failed.Message);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // The agent is stopping.
        }
    }

    private async Task LookAsync(CancellationToken cancellationToken)
    {
        var now = settings.Get();
        var seen = new HashSet<string>();
        if (now.HostDisk.On)
        {
            Check(new(NotificationKind.HostDisk, NotificationKind.HostDisk, "Host", "Disk", NotificationLink.Dashboard), HostUsage.DiskPercent(home.Disk()), now.HostDisk, now.Recovered, seen);
        }

        if (events.Live && (now.HostCpu.On || now.HostMemory.On))
        {
            var runtime = await home.RuntimeAsync(cancellationToken);
            if (now.HostCpu.On)
            {
                Check(new(NotificationKind.HostCpu, NotificationKind.HostCpu, "Host", "CPU", NotificationLink.Dashboard), HostUsage.CpuPercent(runtime), now.HostCpu, now.Recovered, seen);
            }

            if (now.HostMemory.On)
            {
                Check(new(NotificationKind.HostMemory, NotificationKind.HostMemory, "Host", "Memory", NotificationLink.Dashboard), HostUsage.MemoryPercent(runtime), now.HostMemory, now.Recovered, seen);
            }
        }

        if (events.Live && (now.ContainerCpu.On || now.ContainerMemory.On))
        {
            foreach (var container in (await containers.ListAsync(all: false, cancellationToken: cancellationToken)).Containers.Where(c => c.IsRunning))
            {
                if (now.ContainerCpu.On)
                {
                    Check(new($"{NotificationKind.ContainerCpu}:{container.Id}", NotificationKind.ContainerCpu, container.Name, "CPU", NotificationLink.Containers), Percents.Parse(container.CpuPercent), now.ContainerCpu, now.Recovered, seen);
                }

                if (now.ContainerMemory.On)
                {
                    Check(new($"{NotificationKind.ContainerMemory}:{container.Id}", NotificationKind.ContainerMemory, container.Name, "Memory", NotificationLink.Containers), Percents.Parse(container.MemPercent), now.ContainerMemory, now.Recovered, seen);
                }
            }
        }

        // What was not read this beat — switched off, a container gone, the
        // session down — starts afresh when it is read again, without a word.
        foreach (var key in _watches.Keys.Where(key => !seen.Contains(key)).ToList())
        {
            _watches.Remove(key);
        }
    }

    /// <summary>One reading against its threshold; null, not known, says nothing.</summary>
    private void Check(Reading reading, double? value, NotificationThreshold threshold, bool recovered, HashSet<string> seen)
    {
        if (value is not { } percent)
        {
            return;
        }

        seen.Add(reading.Key);
        var at = time.GetUtcNow();
        if (!_watches.TryGetValue(reading.Key, out var watch))
        {
            watch = new Watch();
            _watches[reading.Key] = watch;
        }

        if (percent > threshold.Percent)
        {
            watch.Since ??= at;
            if (!watch.Said && at - watch.Since >= TimeSpan.FromMinutes(threshold.Minutes))
            {
                watch.Said = true;
                var held = threshold.Minutes > 0 ? $" for {threshold.Minutes} min" : "";
                notifier.Raise(reading.Kind, NotificationSeverity.Warning,
                    $"{reading.Subject}: {reading.Measure} at {Percents.Text(Math.Round(percent))}",
                    $"{reading.Measure} has been past {threshold.Percent}%{held}.", reading.Link);
            }

            return;
        }

        if (watch.Said && recovered)
        {
            notifier.Raise(reading.Kind, NotificationSeverity.Info,
                $"{reading.Subject}: {reading.Measure} back to {Percents.Text(Math.Round(percent))}",
                $"{reading.Measure} is under {threshold.Percent}% again.", reading.Link);
        }

        watch.Since = null;
        watch.Said = false;
    }

    /// <summary>What a reading is: its key, its notification's kind, what it is of, the measure, the page it opens.</summary>
    private sealed record Reading(string Key, string Kind, string Subject, string Measure, string Link);

    private sealed class Watch
    {
        public DateTimeOffset? Since { get; set; }

        public bool Said { get; set; }
    }
}
