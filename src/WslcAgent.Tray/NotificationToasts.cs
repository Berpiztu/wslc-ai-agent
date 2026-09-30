using Microsoft.Win32;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Toasts;

namespace WslcAgent.Tray;

/// <summary>
/// The agent's notifications on its own machine:
/// the agent's feed (<see cref="NotificationFeed"/>), from the loopback, which
/// needs no sign-in, shown as Windows toasts (<see cref="WindowsToasts"/>)
/// under the agent's name and picture, a click opening its page in the
/// browser. The last id shown is kept in the registry, one value per agent's
/// port, so a development agent's icon beside the installed one's keeps its own.
/// </summary>
internal sealed class NotificationToasts : IDisposable
{
    private const string TrayKey = @"Software\Berpiztu\wslc-agent\Tray";

    private readonly string _lastValue;
    private readonly HttpClient _http;
    private readonly AgentChanges _changes;
    private readonly NotificationFeed _feed;

    /// <param name="agent">The agent listened to.</param>
    /// <param name="name">What the toasts are grouped under: WSLC AI Agent, and a development agent's port.</param>
    /// <param name="icon">The picture beside them; none, Windows' own.</param>
    public NotificationToasts(Uri agent, string name, Icon? icon)
    {
        _lastValue = $"LastNotification-{agent.Port}";
        _http = new HttpClient { BaseAddress = agent };
        var api = new WslcAgentApi(_http);
        var toasts = new WindowsToasts($"Berpiztu.WslcAiAgent.{agent.Port}", name, icon is null ? null : Picture(icon));
        toasts.Clicked += link => AgentTray.Browse(new Uri(agent, link));
        toasts.Pressed += action => _ = PressAsync(api, action);
        _changes = new AgentChanges(api);
        _feed = new NotificationFeed(api, _changes, ReadLast, Remember, toasts.Show);
    }

    /// <summary>A toast's button: the update cancelled, from the loopback; the agent then says so to every device.</summary>
    private static async Task PressAsync(WslcAgentApi api, string action)
    {
        if (action != NotificationAction.CancelUpdate)
        {
            return;
        }

        try
        {
            await api.CancelAgentUpdateAsync();
        }
        catch (Exception failed) when (failed is HttpRequestException or AgentApiException or TaskCanceledException)
        {
            // The agent is away: the update it announced cannot start without it either.
        }
    }

    /// <summary>The icon as the picture a toast shows, beside the program; null when it cannot be written there.</summary>
    private static string? Picture(Icon icon)
    {
        var picture = Path.Combine(AppContext.BaseDirectory, "notification.png");
        try
        {
            using var bitmap = icon.ToBitmap();
            bitmap.Save(picture, System.Drawing.Imaging.ImageFormat.Png);
            return picture;
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    private void Remember(long id)
    {
        using var key = Registry.CurrentUser.CreateSubKey(TrayKey);
        key.SetValue(_lastValue, id, RegistryValueKind.QWord);
    }

    private long? ReadLast()
    {
        using var key = Registry.CurrentUser.OpenSubKey(TrayKey);
        return key?.GetValue(_lastValue) is long id ? id : null;
    }

    public void Dispose()
    {
        _feed.Dispose();
        _changes.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _http.Dispose();
    }
}
