using Android.App;
using Firebase.Messaging;

namespace WslcAgent.App;

/// <summary>
/// What Firebase tells the app: a new token,
/// which the agent has to be given, and each notification, in front or not:
/// the agent sends data alone, and the app draws it, with its button.
/// </summary>
[Service(Exported = false)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public sealed class PushService : FirebaseMessagingService
{
    // The binding marks onNewToken "deprecated"; Firebase does not, and it is
    // the one way to hear a renewed token (as of 125.1.3).
#pragma warning disable CS0672
    public override void OnNewToken(string token) => AndroidClientNotifications.Renew(token);
#pragma warning restore CS0672

    public override void OnMessageReceived(RemoteMessage message) => AndroidClientNotifications.Show(this, message.Data);
}
