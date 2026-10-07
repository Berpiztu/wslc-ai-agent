using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Notifications;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

/// <summary>
/// The notifications <c>wslc events</c> raises, as each wslc reports an end:
/// up to 3.0.1 <c>kill</c> then <c>stop</c>; from 3.0.2 <c>kill</c>, <c>die</c>,
/// <c>stop</c>, and a container that ends on its own only <c>die</c>. A wslc
/// that changed its words silenced the notification once without anyone
/// noticing, which is why this is tested.
/// </summary>
public sealed class ContainerEventNotificationsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Id = "8449a1cce27f93";

    [Theory]
    [InlineData("stop")]
    [InlineData("die")]
    public async Task An_end_nobody_asked_for_notifies_once_with_its_exit_code(string end)
    {
        var (stops, _, notifier) = Agent();

        stops.Heard(Event(end, exitCode: 1));
        if (end == "die")
        {
            // Docker may still follow it with a stop of its own: the same end.
            stops.Heard(Event("stop"));
        }

        var told = Assert.Single(await RaisedAsync(notifier, 1));
        Assert.Equal(NotificationKind.ContainerStopped, told.Kind);
        Assert.Contains("exit code 1", told.Text);
    }

    [Fact]
    public async Task A_stop_asked_for_says_nothing_in_either_shape()
    {
        var (stops, _, notifier) = Agent();

        // wslc 3.0.1, then wslc 3.0.2, then an end on its own to have something to wait for.
        stops.Heard(Event("kill"));
        stops.Heard(Event("stop", exitCode: 137));
        stops.Heard(Event("kill"));
        stops.Heard(Event("die", exitCode: 137));
        stops.Heard(Event("stop"));
        stops.Heard(Event("die", exitCode: 2));

        var told = Assert.Single(await RaisedAsync(notifier, 1));
        Assert.Contains("exit code 2", told.Text);
    }

    [Fact]
    public async Task Unhealthy_notifies_once_until_the_container_is_healthy_again()
    {
        var (_, health, notifier) = Agent();

        health.Heard(Event("health_status: unhealthy"));
        health.Heard(Event("health_status: unhealthy"));
        health.Heard(Event("health_status: healthy"));
        health.Heard(Event("health_status: unhealthy"));

        var told = await RaisedAsync(notifier, 2);
        Assert.Equal(2, told.Count);
        Assert.All(told, notification => Assert.Equal(NotificationKind.ContainerUnhealthy, notification.Kind));
        Assert.All(told, notification => Assert.Equal(NotificationSeverity.Warning, notification.Severity));
    }

    private (ContainerStops Stops, ContainerHealth Health, Notifier Notifier) Agent()
    {
        var services = factory.Agent(new FakeWslcRunner()).Services;
        return (services.GetRequiredService<ContainerStops>(), services.GetRequiredService<ContainerHealth>(), services.GetRequiredService<Notifier>());
    }

    private static WslcEvent Event(string action, int? exitCode = null) =>
        new(DateTimeOffset.UtcNow, "container", action, Id, exitCode);

    /// <summary>What was raised, once at least <paramref name="expected"/> are: a notification looks up its container's name first, in the background.</summary>
    private static async Task<IReadOnlyList<AgentNotification>> RaisedAsync(Notifier notifier, int expected)
    {
        for (var tries = 0; tries < 50 && notifier.After(0).Notifications.Count < expected; tries++)
        {
            await Task.Delay(20);
        }

        // A moment more, so a notification that should not come has had its chance to.
        await Task.Delay(100);
        return notifier.After(0).Notifications;
    }
}
