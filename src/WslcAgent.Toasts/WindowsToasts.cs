using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Toasts;

/// <summary>
/// The agent's notifications as Windows' own toasts:
/// kept in the notification centre once they leave the screen, grouped under
/// the name of whoever shows them — the icon's balloons showed the same and
/// kept nothing. Windows asks no permission; it
/// lists the name under Settings › System › Notifications, where it can be
/// silenced.
/// <para>
/// An application with no package has no identity of its own, so it is given
/// one here: an AppUserModelID under the user's classes, with the name and the
/// picture a toast shows. No Start menu shortcut is needed. A click is heard
/// while the program runs, on screen or from the notification centre.
/// </para>
/// </summary>
public sealed class WindowsToasts
{
    private const string IdentitiesKey = @"Software\Classes\AppUserModelId";

    /// <summary>A clicked toast is heard while it is alive: the last ones are kept, for the notification centre's clicks.</summary>
    private const int Kept = 50;

    private readonly ToastNotifier _notifier;
    private readonly Queue<ToastNotification> _shown = new();

    /// <param name="identity">The AppUserModelID, one per program and agent, so their toasts are not mixed.</param>
    /// <param name="name">What the toasts are grouped under.</param>
    /// <param name="picture">The image beside them, a file on disk; null for Windows' own.</param>
    public WindowsToasts(string identity, string name, string? picture)
    {
        // Written at every start, so a moved folder follows.
        using (var key = Registry.CurrentUser.CreateSubKey($@"{IdentitiesKey}\{identity}"))
        {
            key.SetValue("DisplayName", name);
            if (picture is not null)
            {
                key.SetValue("IconUri", picture);
            }
        }

        _notifier = ToastNotificationManager.CreateToastNotifier(identity);
    }

    /// <summary>What a button's arguments begin with, to tell a button from a click on the toast, whose arguments are its page.</summary>
    private const string ButtonPrefix = "action:";

    /// <summary>A toast was clicked: its page, relative to the agent; empty for none. Raised on whatever thread Windows chose.</summary>
    public event Action<string>? Clicked;

    /// <summary>A toast's button was pressed: its action, one of <see cref="NotificationAction"/>. Raised on whatever thread Windows chose.</summary>
    public event Action<string>? Pressed;

    /// <summary>What <see cref="NotificationFeed"/> hands over: one toast each, and one before them saying how many older ones were left out.</summary>
    public void Show(IReadOnlyList<AgentNotification> notifications, int leftOut)
    {
        if (leftOut > 0)
        {
            Show($"{leftOut} more notifications", "Older ones, left out; the agent's log has them all.", "", "");
        }

        foreach (var notification in notifications)
        {
            Show(notification.Title, notification.Text, notification.Link, notification.Action);
        }
    }

    /// <summary>
    /// One toast. One with a button stays on screen until it is answered (a
    /// reminder, with Dismiss beside the button): the update's minute is for
    /// deciding, and a toast gone in five seconds took the question with it.
    /// </summary>
    private void Show(string title, string text, string link, string action)
    {
        var buttons = action.Length == 0 ? "" :
            $"<actions><action content=\"{SecurityElement.Escape(NotificationAction.Label(action))}\" arguments=\"{ButtonPrefix}{SecurityElement.Escape(action)}\" />" +
            "<action content=\"Dismiss\" arguments=\"dismiss\" activationType=\"system\" /></actions>";
        var xml = new XmlDocument();
        xml.LoadXml(
            $"<toast launch=\"{SecurityElement.Escape(link)}\"{(action.Length == 0 ? "" : " scenario=\"reminder\"")}><visual><binding template=\"ToastGeneric\">" +
            $"<text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(text)}</text>" +
            $"</binding></visual>{buttons}</toast>");
        var toast = new ToastNotification(xml);
        toast.Activated += (_, activated) =>
        {
            if ((activated as ToastActivatedEventArgs)?.Arguments is { } arguments && arguments.StartsWith(ButtonPrefix, StringComparison.Ordinal))
            {
                Pressed?.Invoke(arguments[ButtonPrefix.Length..]);
            }
            else
            {
                Clicked?.Invoke(link);
            }
        };
        lock (_shown)
        {
            _shown.Enqueue(toast);
            while (_shown.Count > Kept)
            {
                _shown.Dequeue();
            }
        }

        _notifier.Show(toast);
    }
}
