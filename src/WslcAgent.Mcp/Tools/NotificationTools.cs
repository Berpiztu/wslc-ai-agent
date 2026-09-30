using System.ComponentModel;
using ModelContextProtocol.Server;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp.Tools;

/// <summary>The agent's notifications: what it notifies, and what it raised lately.</summary>
[McpServerToolType]
public static class NotificationTools
{
    [McpServerTool(Name = "list_notifications", ReadOnly = true)]
    [Description("The notifications the agent raised (a container stopped on its own, a disk filling up, a job that failed…), oldest first, with the last id raised. after=0 gives every one kept (the last two hundred); pass the last id seen to get only the new ones.")]
    public static NotificationList ListNotifications(
        INotificationService notifications,
        [Description("Only those raised after this id; 0 for all kept.")] long after = 0) =>
        notifications.After(after);

    [McpServerTool(Name = "get_notification_settings", ReadOnly = true)]
    [Description("Settings › Notifications: which notifications are on, and for the readings (host disk, memory and CPU, any container's memory and CPU) the percent past which, and the minutes for which, a reading notifies.")]
    public static NotificationSettings GetNotificationSettings(INotificationService notifications) =>
        notifications.Settings();

    [McpServerTool(Name = "set_notification_settings")]
    [Description("Saves Settings › Notifications whole: read it with get_notification_settings, change what the user asked for, and send it all back. A threshold is { on, percent, minutes }; minutes 0 notifies at once.")]
    public static NotificationSettings SetNotificationSettings(
        INotificationService notifications,
        [Description("The settings, whole, as get_notification_settings returns them.")] NotificationSettings settings) =>
        notifications.Save(settings);
}
