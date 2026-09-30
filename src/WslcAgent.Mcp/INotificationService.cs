using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>
/// The agent's notifications: Settings ›
/// Notifications, and what was raised. Implemented by the server, which
/// watches the host, the containers and its own jobs whether a client is
/// open or not.
/// </summary>
public interface INotificationService
{
    /// <summary>What the agent notifies, as Settings › Notifications holds it.</summary>
    NotificationSettings Settings();

    /// <summary>Saves what the agent notifies, from its next reading on.</summary>
    NotificationSettings Save(NotificationSettings settings);

    /// <summary>The notifications raised after <paramref name="after"/>, oldest first, and the last id raised.</summary>
    NotificationList After(long after);
}
