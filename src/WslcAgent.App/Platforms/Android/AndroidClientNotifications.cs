using Android.App;
using Android.Content;
using Android.Gms.Extensions;
using AndroidX.Core.App;
using Firebase.Messaging;
using Microsoft.Extensions.Logging;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Notifications;
using Channels = WslcAgent.ApiClient.Contracts.NotificationChannel;

namespace WslcAgent.App;

/// <summary>
/// The agent's notifications on this phone:
/// pushed by the agent through Firebase Cloud Messaging as data, and drawn
/// here, in front or not (<see cref="PushService"/>), on the channel of its
/// severity and with its button when it has one. A tap starts the app, or
/// brings it back, with the notification's page, which <see cref="MainActivity"/>
/// hands here for the UI to open.
/// <para>
/// Firebase's service and the activity are made by Android, not by the
/// container, so what they report goes through the static side of this class
/// to the one instance the UI holds.
/// </para>
/// </summary>
internal sealed class AndroidClientNotifications(ILogger<AndroidClientNotifications> logger) : IClientNotifications
{
    private static string? _opened;

    private static event Action<string>? Renewed;

    private static event Action? Tapped;

    public bool Supported => true;

    /// <summary>Pushed by the agent through Firebase, running or not: never read from the feed.</summary>
    public bool ShowsFeed => false;

    public long? LastShown { get; set; }

    public void Show(IReadOnlyList<AgentNotification> notifications, int leftOut)
    {
    }

    /// <summary>A button's work is done by <see cref="NotificationActionReceiver"/>, the app open or not.</summary>
    public event Action<string>? Pressed
    {
        add { }
        remove { }
    }

    public string DeviceName => $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}".Trim();

    public string Platform => "android";

    public event Action<string>? TokenChanged
    {
        add => Renewed += value;
        remove => Renewed -= value;
    }

    public event Action? Opened
    {
        add => Tapped += value;
        remove => Tapped -= value;
    }

    public string? TakeOpened() => Interlocked.Exchange(ref _opened, null);

    public async Task<string?> TokenAsync(CancellationToken cancellationToken = default)
    {
        EnsureChannels(Android.App.Application.Context);
        // Android 13 made showing notifications a permission the user grants;
        // before it they are simply shown.
        if (OperatingSystem.IsAndroidVersionAtLeast(33)
            && await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.PostNotifications>()) != PermissionStatus.Granted)
        {
            return null;
        }

        try
        {
            // The binding marks getToken "deprecated"; Firebase does not, and
            // it is the one way to ask for the token (as of 125.1.3).
#pragma warning disable CS0618
            return (await FirebaseMessaging.Instance.GetToken().AsAsync<Java.Lang.Object>())?.ToString();
#pragma warning restore CS0618
        }
        catch (Java.Lang.Exception failed)
        {
            // No Google Play services, or Firebase not reachable: this phone
            // gets no notifications, and the rest of the app does not care.
            logger.LogWarning("notifications: no Firebase token: {Message}", failed.Message);
            return null;
        }
    }

    /// <summary>Firebase renewed the token (<see cref="PushService"/>).</summary>
    public static void Renew(string token) => Renewed?.Invoke(token);

    /// <summary>
    /// A notification was tapped: its page waits for the UI, which may not be
    /// there yet when the tap started the app (<see cref="MainActivity"/>).
    /// </summary>
    public static void Open(Intent? intent)
    {
        if (intent?.GetStringExtra(NotificationData.Link) is { Length: > 0 } link)
        {
            _opened = link;
            intent.RemoveExtra(NotificationData.Link);
            Tapped?.Invoke();
        }
    }

    /// <summary>
    /// A notification the agent pushed, drawn by the app in front or not
    /// (<see cref="PushService"/>): on its channel, the tap bringing the app
    /// with its page, and its button, when it has one, doing its work without
    /// opening the app (<see cref="NotificationActionReceiver"/>).
    /// </summary>
    public static void Show(Context context, IDictionary<string, string> data)
    {
        EnsureChannels(context);
        var title = data.TryGetValue(NotificationData.Title, out var saidTitle) ? saidTitle : "";
        var text = data.TryGetValue(NotificationData.Text, out var saidText) ? saidText : "";
        var severity = data.TryGetValue(NotificationData.Severity, out var said) ? said : NotificationSeverity.Warning;
        var open = new Intent(context, typeof(MainActivity));
        open.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        if (data.TryGetValue(NotificationData.Link, out var link))
        {
            open.PutExtra(NotificationData.Link, link);
        }

        var id = data.TryGetValue(NotificationData.Id, out var number) && int.TryParse(number, out var parsed) ? parsed : Environment.TickCount;
        var tap = PendingIntent.GetActivity(context, id, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        // One call each: the binding says every setter may return null, which
        // a chain would have to check at every step.
        var builder = new NotificationCompat.Builder(context, Channels.Of(severity));
        builder.SetSmallIcon(Resource.Drawable.notification_icon);
        builder.SetContentTitle(title);
        builder.SetContentText(text);
        builder.SetStyle(new NotificationCompat.BigTextStyle().BigText(text));
        builder.SetContentIntent(tap);
        builder.SetAutoCancel(true);
        if (data.TryGetValue(NotificationData.Action, out var action) && action == NotificationAction.CancelUpdate)
        {
            var press = new Intent(context, typeof(NotificationActionReceiver));
            press.PutExtra(NotificationData.Action, action);
            press.PutExtra(NotificationActionReceiver.NotificationId, id);
            builder.AddAction(0, NotificationAction.Label(action),
                PendingIntent.GetBroadcast(context, id, press, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent));
        }

        NotificationManagerCompat.From(context)?.Notify(id, builder.Build());
    }

    /// <summary>
    /// The update cancelled from its notification's button, the app open or
    /// not: the agent asked with the session this phone keeps, and the
    /// notification taken down; one that could not reach the agent says why
    /// in its place. The other devices hear it was cancelled from the agent.
    /// </summary>
    public static async Task CancelUpdateAsync(Context context, int id)
    {
        AgentAddress.Resolve();
        var access = new AgentAccessToken { Value = new PreferencesTokenStore().Load() };
        using var http = new HttpClient(new AgentAccessHandler(access) { InnerHandler = new HttpClientHandler() })
        {
            BaseAddress = new Uri(AgentAddress.Current),
            Timeout = TimeSpan.FromSeconds(20),
        };
        try
        {
            await new WslcAgentApi(http).CancelAgentUpdateAsync();
            NotificationManagerCompat.From(context)?.Cancel(id);
        }
        catch (Exception failed) when (failed is HttpRequestException or AgentApiException or TaskCanceledException)
        {
            Show(context, new Dictionary<string, string>
            {
                [NotificationData.Id] = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [NotificationData.Severity] = NotificationSeverity.Error,
                [NotificationData.Title] = "Update not cancelled",
                [NotificationData.Text] = $"The agent could not be asked: {failed.Message}",
                [NotificationData.Link] = NotificationLink.Settings,
            });
        }
    }

    /// <summary>
    /// The two channels the agent sends to, so the phone can silence one
    /// without the other: alerts, which sound; information, which does not.
    /// Creating one that exists changes nothing.
    /// </summary>
    private static void EnsureChannels(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26) || context.GetSystemService(Context.NotificationService) is not NotificationManager manager)
        {
            return;
        }

        manager.CreateNotificationChannel(new Android.App.NotificationChannel(Channels.Alerts, "Alerts", NotificationImportance.High)
        {
            Description = "A container down, a disk filling up, a job that failed",
        });
        manager.CreateNotificationChannel(new Android.App.NotificationChannel(Channels.Info, "Information", NotificationImportance.Low)
        {
            Description = "A job finished, a reading back to normal, the agent updated",
        });
    }
}
