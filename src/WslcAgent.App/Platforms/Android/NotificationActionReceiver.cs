using Android.Content;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.App;

/// <summary>
/// A notification's button pressed: its work done
/// without opening the app — the update cancelled — kept alive by
/// <c>GoAsync</c> while the agent is asked.
/// </summary>
[BroadcastReceiver(Exported = false)]
public sealed class NotificationActionReceiver : BroadcastReceiver
{
    /// <summary>The extra holding the notification the button is on, taken down once its work is done.</summary>
    public const string NotificationId = "notification_id";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.GetStringExtra(NotificationData.Action) != NotificationAction.CancelUpdate)
        {
            return;
        }

        var agent = intent.GetStringExtra(NotificationData.Agent);
        var id = intent.GetIntExtra(NotificationId, 0);
        var pending = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                await AndroidClientNotifications.CancelUpdateAsync(context, agent, id);
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}
