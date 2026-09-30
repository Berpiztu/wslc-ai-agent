using WslcAgent.ApiClient.Contracts;
using WslcAgent.Toasts;
using WslcAgent.UI.Notifications;

namespace WslcAgent.App;

/// <summary>
/// The agent's notifications in the Windows client: the feed of the agent it talks to, shown as
/// Windows toasts under the client's name while the client runs — minimized
/// too — when that agent is on another machine. On the agent's own machine its
/// tray icon shows them, and the client showing them as well would say each
/// one twice. A click brings the client to the front on the notification's
/// page. The last one shown is kept per agent, so the client started again
/// shows what arrived while it was closed, and nothing twice.
/// </summary>
internal sealed class WindowsClientNotifications : IClientNotifications
{
    private const string Identity = "Berpiztu.WslcAiClient";

    private readonly Uri _agent = new(AgentAddress.Current);
    private readonly Lazy<WindowsToasts> _toasts;
    private string? _opened;

    public WindowsClientNotifications()
    {
        _toasts = new(() =>
        {
            var toasts = new WindowsToasts(Identity, "WSLC AI Client", Picture());
            toasts.Clicked += OnClicked;
            toasts.Pressed += action => Pressed?.Invoke(action);
            return toasts;
        });
    }

    public bool Supported => true;

    public bool ShowsFeed => !_agent.IsLoopback;

    public string DeviceName => Environment.MachineName;

    public string Platform => "windows";

    /// <summary>The last shown, kept under the agent's address: each agent numbers its own.</summary>
    public long? LastShown
    {
        get => Preferences.Default.Get(LastKey, -1L) is var id && id >= 0 ? id : null;
        set
        {
            if (value is { } id)
            {
                Preferences.Default.Set(LastKey, id);
            }
            else
            {
                Preferences.Default.Remove(LastKey);
            }
        }
    }

    private string LastKey => $"notifications.last.{_agent.Authority}";

    public void Show(IReadOnlyList<AgentNotification> notifications, int leftOut) => _toasts.Value.Show(notifications, leftOut);

    /// <summary>Nothing is pushed to Windows: the client reads the feed while it runs.</summary>
    public Task<string?> TokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

    public event Action<string>? TokenChanged
    {
        add { }
        remove { }
    }

    public event Action? Opened;

    /// <summary>A toast's button: done by the UI with the agent it is signed in to (ClientNotificationsWatcher).</summary>
    public event Action<string>? Pressed;

    public string? TakeOpened() => Interlocked.Exchange(ref _opened, null);

    /// <summary>A toast clicked: the client to the front, on the notification's page.</summary>
    private void OnClicked(string link)
    {
        _opened = link;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            BringToFront();
            Opened?.Invoke();
        });
    }

    private static void BringToFront()
    {
        if (Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window window)
        {
            return;
        }

        if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        window.Activate();
    }

    /// <summary>The client's own icon beside its toasts, as its build writes it; none, Windows' own.</summary>
    private static string? Picture()
    {
        var picture = Path.Combine(AppContext.BaseDirectory, "appiconLogo.targetsize-48.png");
        return File.Exists(picture) ? picture : null;
    }
}
