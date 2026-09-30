using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Notifications;

/// <summary>
/// The agent's notifications on a native client,
/// two ways. The Android client gets a Firebase token, which the agent pushes
/// to, whether the app runs or not. The Windows client shows the agent's
/// feed itself while it runs (<see cref="ShowsFeed"/>), when its agent is on
/// another machine — on the agent's own, the tray icon shows them. Both open
/// the page of a notification the user tapped. The browser has none of this
/// and reports <see cref="Supported"/> false.
/// </summary>
public interface IClientNotifications
{
    /// <summary>False in the browser.</summary>
    bool Supported { get; }

    /// <summary>
    /// This client shows the agent's notifications itself, from the events
    /// stream, while it runs: the Windows client, with its agent on another
    /// machine. False where they are pushed (Android) or shown by the tray icon.
    /// </summary>
    bool ShowsFeed { get; }

    /// <summary>The last notification shown from this agent's feed, kept on the device; null when none ever was.</summary>
    long? LastShown { get; set; }

    /// <summary>Shows what the feed handed over: the newest, oldest first, and how many older were left out.</summary>
    void Show(IReadOnlyList<AgentNotification> notifications, int leftOut);

    /// <summary>
    /// A shown notification's button was pressed: its action, one of
    /// <see cref="NotificationAction"/>, for the UI to do with the agent it is
    /// signed in to. Android does its own, the app closed or not.
    /// </summary>
    event Action<string>? Pressed;

    /// <summary>What the device is called in Settings › Notifications › Phones: its maker and model.</summary>
    string DeviceName { get; }

    /// <summary><c>android</c>.</summary>
    string Platform { get; }

    /// <summary>
    /// The token the agent pushes to, asking the user the first time whether
    /// the app may show notifications; null when the user said no, or the
    /// phone cannot receive them (no Google Play services).
    /// </summary>
    Task<string?> TokenAsync(CancellationToken cancellationToken = default);

    /// <summary>Firebase gave the device a new token: the agent has to be told, or its pushes go nowhere.</summary>
    event Action<string>? TokenChanged;

    /// <summary>A notification was tapped: <see cref="TakeOpened"/> has its page.</summary>
    event Action? Opened;

    /// <summary>The page of the last notification tapped, once; null when there is none waiting.</summary>
    string? TakeOpened();
}

/// <summary>The browser's: nothing is pushed to it, and it shows nothing.</summary>
public sealed class NoClientNotifications : IClientNotifications
{
    public bool Supported => false;

    public bool ShowsFeed => false;

    public long? LastShown { get; set; }

    public void Show(IReadOnlyList<AgentNotification> notifications, int leftOut)
    {
    }

    public event Action<string>? Pressed
    {
        add { }
        remove { }
    }

    public string DeviceName => "";

    public string Platform => "";

    public Task<string?> TokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

    public event Action<string>? TokenChanged
    {
        add { }
        remove { }
    }

    public event Action? Opened
    {
        add { }
        remove { }
    }

    public string? TakeOpened() => null;
}
