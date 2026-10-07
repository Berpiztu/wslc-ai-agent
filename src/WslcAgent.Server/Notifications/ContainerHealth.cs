using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Notifications;

/// <summary>
/// A container whose health check fails, heard from <c>wslc events</c>
/// (wslc 3.0.2 on): <c>health_status: unhealthy</c> notifies, once until the
/// container is healthy again, and its <c>health_status: healthy</c> after
/// that says so when Settings › Notifications asks to hear what is back.
/// </summary>
public sealed class ContainerHealth(Notifier notifier, ContainerNames names)
{
    private const string Unhealthy = "health_status: unhealthy";
    private const string Healthy = "health_status: healthy";

    /// <summary>The containers told unhealthy, not healthy again since.</summary>
    private readonly HashSet<string> _unhealthy = new(StringComparer.Ordinal);

    private readonly Lock _gate = new();

    /// <summary>One event, as the reader reads it: a health status that changed what was told notifies.</summary>
    public void Heard(WslcEvent heard)
    {
        if (heard.Type != "container" || heard.Action is not (Unhealthy or Healthy))
        {
            return;
        }

        lock (_gate)
        {
            var told = heard.Action == Unhealthy ? _unhealthy.Add(heard.Id) : _unhealthy.Remove(heard.Id);
            if (!told)
            {
                return;
            }
        }

        _ = NotifyAsync(heard.Id, heard.Action == Unhealthy);
    }

    private async Task NotifyAsync(string id, bool unhealthy)
    {
        if (!unhealthy && !notifier.Settings().Recovered)
        {
            return;
        }

        var name = await names.OfAsync(id);
        if (unhealthy)
        {
            notifier.Raise(NotificationKind.ContainerUnhealthy, NotificationSeverity.Warning,
                $"{name} unhealthy", "Its health check is failing.", NotificationLink.Containers);
        }
        else
        {
            notifier.Raise(NotificationKind.ContainerUnhealthy, NotificationSeverity.Info,
                $"{name} healthy again", "Its health check passes again.", NotificationLink.Containers);
        }
    }
}
