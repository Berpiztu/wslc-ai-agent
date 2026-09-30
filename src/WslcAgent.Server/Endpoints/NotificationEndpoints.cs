using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Notifications;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/notifications</c>: what the agent raised, and Settings → Notifications. See docs/api-v1.md.</summary>
public static class NotificationEndpoints
{
    public static RouteGroupBuilder MapNotificationEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/notifications");

        // Those after the last one a client saw; the events stream says when there are new ones.
        group.MapGet("", (INotificationService notifications, long after = 0) => notifications.After(after))
            .WithName("ListNotifications");

        group.MapGet("/settings", (INotificationService notifications) => notifications.Settings())
            .WithName("GetNotificationSettings");

        group.MapPut("/settings", (NotificationSettings settings, INotificationService notifications) => notifications.Save(settings))
            .WithName("SetNotificationSettings");

        // The phones they are pushed to through Firebase, and whether the agent has the key to push with.
        group.MapGet("/devices", (NotificationDeviceStore devices, FirebasePush push) =>
                new NotificationDevices(push.Project is not null, push.Project ?? "", push.KeyFile, devices.List()))
            .WithName("ListNotificationDevices");

        // A phone registers at every start of the app: the same token again only refreshes it.
        group.MapPost("/devices", (RegisterDeviceRequest request, NotificationDeviceStore devices) => devices.Register(request))
            .WithName("RegisterNotificationDevice");

        group.MapDelete("/devices/{id}", (string id, NotificationDeviceStore devices) =>
                devices.Remove(id) ? Results.NoContent() : Results.NotFound())
            .WithName("RemoveNotificationDevice");

        return api;
    }
}
