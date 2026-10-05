namespace WslcAgent.ApiClient.Contracts;

/// <summary>
/// One of the agent's notifications: something
/// happened that someone has to know of while no screen is open — a container
/// that stopped on its own, a disk filling up, a job that failed. The system's,
/// not a dashboard's: raised by the agent from Settings › Notifications,
/// whatever any client shows.
/// </summary>
/// <param name="Id">Rises by one with each notification, for good: a client that was away asks for those after the last it saw.</param>
/// <param name="Time">When the agent raised it.</param>
/// <param name="Kind">What it is about, one of <see cref="NotificationKind"/>.</param>
/// <param name="Severity">How it is shown, one of <see cref="NotificationSeverity"/>.</param>
/// <param name="Title">One line, what happened.</param>
/// <param name="Text">What to know about it: the container, the reading, the error.</param>
/// <param name="Link">The page it opens, relative to the agent, one of <see cref="NotificationLink"/>; empty for none.</param>
/// <param name="Action">What its one button does, one of <see cref="NotificationAction"/>, without opening the application; empty for no button.</param>
public sealed record AgentNotification(long Id, DateTimeOffset Time, string Kind, string Severity, string Title, string Text, string Link = "", string Action = "");

/// <summary>
/// What a notification's button does, done by whoever shows it without the
/// application being opened (the update's
/// countdown answered from the notification itself).
/// </summary>
public static class NotificationAction
{
    /// <summary>Cancels the agent's update that was announced: <c>POST /api/v1/agent/update/cancel</c>.</summary>
    public const string CancelUpdate = "cancel-update";

    /// <summary>The button's word.</summary>
    public static string Label(string action) => action == CancelUpdate ? "Cancel update" : action;
}

/// <summary>Body of <c>GET /api/v1/notifications</c>: those after the id asked for, oldest first, and the last id raised.</summary>
/// <param name="Notifications">The notifications kept after the id asked for; the agent keeps the last two hundred.</param>
/// <param name="Latest">The id of the last notification raised, 0 while there is none: where a client that has seen nothing starts from.</param>
public sealed record NotificationList(IReadOnlyList<AgentNotification> Notifications, long Latest);

/// <summary>What a notification is about.</summary>
public static class NotificationKind
{
    public const string HostDisk = "host-disk";
    public const string HostMemory = "host-memory";
    public const string HostCpu = "host-cpu";
    public const string ContainerMemory = "container-memory";
    public const string ContainerCpu = "container-cpu";
    public const string ContainerStopped = "container-stopped";
    public const string ContainerNotRestarted = "container-not-restarted";
    public const string JobFailed = "job-failed";
    public const string JobFinished = "job-finished";
    public const string UpdateFailed = "update-failed";
    public const string UpdateInstalled = "update-installed";
    public const string UpdateAnnounced = "update-announced";
    public const string UpdateCancelled = "update-cancelled";
    public const string SessionLost = "session-lost";
}

/// <summary>How a notification is shown.</summary>
public static class NotificationSeverity
{
    /// <summary>Something broke: a container down, a job failed.</summary>
    public const string Error = "error";

    /// <summary>Something is about to break: a reading past its threshold.</summary>
    public const string Warning = "warning";

    /// <summary>Something went well, or came back: a job finished, a reading back under its threshold.</summary>
    public const string Info = "info";
}

/// <summary>
/// A phone the agent's notifications reach through Firebase Cloud Messaging:
/// the agent hands each one to Google, which
/// wakes the phone, so the phone need not be connected to the agent to get
/// it — only once, to register. Its Firebase token is the agent's alone and
/// never leaves it.
/// </summary>
/// <param name="Id">The agent's name for it, what removing it takes.</param>
/// <param name="Name">What the device calls itself: its model.</param>
/// <param name="Platform">android.</param>
/// <param name="Registered">When it first registered.</param>
/// <param name="LastSeen">When it last registered again, which it does at every start.</param>
public sealed record NotificationDevice(string Id, string Name, string Platform, DateTimeOffset Registered, DateTimeOffset LastSeen);

/// <summary>
/// Body of <c>POST /api/v1/notifications/devices</c>: a device, the Firebase
/// token its notifications go to, and the address it knows this agent by,
/// which every push carries back so a phone signed in to several agents tells
/// their notifications apart and opens the right one.
/// </summary>
public sealed record RegisterDeviceRequest(string Name, string Platform, string Token, string? Agent = null);

/// <summary>
/// Body of <c>GET /api/v1/notifications/devices</c>: whether the agent can
/// send to phones, and the phones it sends to.
/// </summary>
/// <param name="Push">The agent has a Firebase key and sends: without it the devices are kept and nothing is sent.</param>
/// <param name="Project">The Firebase project the key is for; empty without one.</param>
/// <param name="KeyFile">Where the agent looks for its key: the service account's JSON, kept on this machine and nowhere else.</param>
/// <param name="Devices">The phones registered.</param>
public sealed record NotificationDevices(bool Push, string Project, string KeyFile, IReadOnlyList<NotificationDevice> Devices);

/// <summary>The Android notification channels a notification goes to, so the phone can silence one without the other.</summary>
public static class NotificationChannel
{
    /// <summary>Something broke or is about to: errors and warnings.</summary>
    public const string Alerts = "alerts";

    /// <summary>Something went well, or came back.</summary>
    public const string Info = "info";

    public static string Of(string severity) => severity == NotificationSeverity.Info ? Info : Alerts;
}

/// <summary>
/// The page a notification opens: the list of what it is about, never one
/// object's details (a container's opened its
/// details on the Logs tab, and the list is where to look from).
/// </summary>
public static class NotificationLink
{
    public const string Containers = "containers";
    public const string Images = "images";
    public const string Dashboard = "dashboard";
    public const string System = "system";
    public const string Settings = "settings";
}

/// <summary>The keys of what a pushed notification carries besides its title and text, for the app to act on a tap.</summary>
public static class NotificationData
{
    /// <summary>
    /// The agent it comes from, as the device gave its address when it
    /// registered; empty from a device registered before it said one, which is
    /// then taken for the agent the app is on.
    /// </summary>
    public const string Agent = "agent";

    public const string Id = "id";
    public const string Kind = "kind";
    public const string Severity = "severity";

    /// <summary>The page a tap opens, relative to the agent.</summary>
    public const string Link = "link";

    /// <summary>What it says: the agent sends data alone, and the app draws the notification, so it can carry its button.</summary>
    public const string Title = "title";
    public const string Text = "text";

    /// <summary>Its button, one of <see cref="NotificationAction"/>; empty for none.</summary>
    public const string Action = "action";
}

/// <summary>A reading that notifies: past <paramref name="Percent"/> for <paramref name="Minutes"/> minutes (0: at once).</summary>
public sealed record NotificationThreshold(bool On, int Percent, int Minutes);

/// <summary>
/// Settings › Notifications (<c>GET</c> and <c>PUT /api/v1/notifications/settings</c>):
/// what the agent notifies, the same for every client. The defaults:
/// on, what needs someone to act; off, what only
/// says that something went well.
/// </summary>
/// <param name="HostDisk">The drive the sessions are kept on.</param>
/// <param name="HostMemory">The containers' memory used of the session's.</param>
/// <param name="HostCpu">The containers' CPU used of the machine's.</param>
/// <param name="ContainerMemory">Any container's memory, of its limit: at its limit it is killed.</param>
/// <param name="ContainerCpu">Any container's CPU: working hard is usually its job, so off by default.</param>
/// <param name="ContainerStopped">A container stopped without being asked to: <c>wslc events</c> says it stopped, and no one killed it first.</param>
/// <param name="ContainerNotRestarted">The restart policy could not start a container.</param>
/// <param name="JobFailed">A transfer, pull, build or backup failed.</param>
/// <param name="JobFinished">One of them finished, however short.</param>
/// <param name="UpdateFailed">The agent's update failed.</param>
/// <param name="UpdateInstalled">The agent updated itself.</param>
/// <param name="SessionLost">The WSLC session went down without the user stopping it from the agent.</param>
/// <param name="Recovered">A reading that notified is back under its threshold.</param>
/// <param name="UpdateAnnounced">The agent is about to update itself, with a minute to cancel it from the notification: on unless switched off, a settings file saved before it existed included.</param>
/// <param name="UpdateCancelled">An announced update was cancelled, from any client.</param>
public sealed record NotificationSettings(
    NotificationThreshold HostDisk,
    NotificationThreshold HostMemory,
    NotificationThreshold HostCpu,
    NotificationThreshold ContainerMemory,
    NotificationThreshold ContainerCpu,
    bool ContainerStopped,
    bool ContainerNotRestarted,
    bool JobFailed,
    bool JobFinished,
    bool UpdateFailed,
    bool UpdateInstalled,
    bool SessionLost,
    bool Recovered,
    bool UpdateAnnounced = true,
    bool UpdateCancelled = true)
{
    // Every reading at once, 0 minutes, while notifications are being tried;
    // the minutes come back once they are.
    public static readonly NotificationSettings Defaults = new(
        HostDisk: new(true, 90, 0),
        HostMemory: new(true, 90, 0),
        HostCpu: new(true, 95, 0),
        ContainerMemory: new(true, 90, 0),
        ContainerCpu: new(false, 95, 0),
        ContainerStopped: true,
        ContainerNotRestarted: true,
        JobFailed: true,
        JobFinished: false,
        UpdateFailed: true,
        UpdateInstalled: false,
        SessionLost: true,
        Recovered: false);

    /// <summary>Whether a notification of this kind is on.</summary>
    public bool Allows(string kind) => kind switch
    {
        NotificationKind.HostDisk => HostDisk.On,
        NotificationKind.HostMemory => HostMemory.On,
        NotificationKind.HostCpu => HostCpu.On,
        NotificationKind.ContainerMemory => ContainerMemory.On,
        NotificationKind.ContainerCpu => ContainerCpu.On,
        NotificationKind.ContainerStopped => ContainerStopped,
        NotificationKind.ContainerNotRestarted => ContainerNotRestarted,
        NotificationKind.JobFailed => JobFailed,
        NotificationKind.JobFinished => JobFinished,
        NotificationKind.UpdateFailed => UpdateFailed,
        NotificationKind.UpdateInstalled => UpdateInstalled,
        NotificationKind.UpdateAnnounced => UpdateAnnounced,
        NotificationKind.UpdateCancelled => UpdateCancelled,
        NotificationKind.SessionLost => SessionLost,
        _ => false,
    };
}
