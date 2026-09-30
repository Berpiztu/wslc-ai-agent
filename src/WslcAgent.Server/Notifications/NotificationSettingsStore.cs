using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Host;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Notifications;

/// <summary>Settings → Notifications, kept in <c>notifications.json</c> in the agent's data folder; the defaults until it exists.</summary>
public sealed class NotificationSettingsStore(IOptions<WslcOptions> options)
    : SavedSettings<NotificationSettings>(
        Path.Combine(options.Value.DataDirectory, "notifications.json"),
        NotificationSettings.Defaults);
