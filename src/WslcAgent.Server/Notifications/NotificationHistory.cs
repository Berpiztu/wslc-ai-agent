using System.Text.Json;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Notifications;

/// <summary>
/// The notifications raised lately, kept in <c>notifications-history.json</c>
/// in the agent's data folder so they outlive the agent: a client that was
/// away — the tray icon while the agent updated itself — asks for those after
/// the last it saw, and the ids go on rising across restarts, so that "after"
/// never skips one.
/// </summary>
public sealed class NotificationHistory
{
    /// <summary>Enough for a client away for a weekend; notifications are rare.</summary>
    private const int Kept = 200;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;
    private readonly Lock _gate = new();
    private readonly List<AgentNotification> _items;
    private long _latest;

    public NotificationHistory(IOptions<WslcOptions> options)
    {
        _path = Path.Combine(options.Value.DataDirectory, "notifications-history.json");
        var read = Read();
        _items = [.. read?.Notifications ?? []];
        _latest = read?.Latest ?? 0;
    }

    /// <summary>A notification with the next id, kept and written.</summary>
    public AgentNotification Add(string kind, string severity, string title, string text, string link, string action)
    {
        lock (_gate)
        {
            var notification = new AgentNotification(++_latest, DateTimeOffset.Now, kind, severity, title, text, link, action);
            _items.Add(notification);
            if (_items.Count > Kept)
            {
                _items.RemoveRange(0, _items.Count - Kept);
            }

            Write();
            return notification;
        }
    }

    /// <summary>Those raised after <paramref name="after"/>, oldest first, and the last id raised.</summary>
    public NotificationList After(long after)
    {
        lock (_gate)
        {
            return new NotificationList([.. _items.Where(n => n.Id > after)], _latest);
        }
    }

    /// <summary>Called under <see cref="_gate"/>. A write that fails keeps the list in memory: the notification still goes out.</summary>
    private void Write()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(new NotificationList(_items, _latest), JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The next one writes the whole list again.
        }
    }

    /// <summary>A file written by hand, or half-written, starts the list empty; never the agent stopped.</summary>
    private NotificationList? Read()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<NotificationList>(File.ReadAllText(_path), JsonOptions) : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
