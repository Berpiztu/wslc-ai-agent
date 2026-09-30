using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Host;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Notifications;

/// <summary>
/// The phones the agent's notifications are pushed to, kept in
/// <c>notification-devices.json</c> in the agent's data folder with the
/// Firebase token of each, which never leaves the agent. A device registers at
/// every start of the app; the same token again only refreshes it, and a token
/// Firebase no longer knows is forgotten on the first push that finds out.
/// </summary>
public sealed class NotificationDeviceStore(IOptions<WslcOptions> options, TimeProvider time)
    : SavedSettings<NotificationDeviceStore.Saved>(Path.Combine(options.Value.DataDirectory, "notification-devices.json"), new Saved([]))
{
    private readonly Lock _gate = new();

    /// <summary>The devices, as the agent shows them: without their tokens.</summary>
    public IReadOnlyList<NotificationDevice> List() => [.. Get().Devices.Select(d => d.Shown())];

    /// <summary>The Firebase tokens to push to.</summary>
    public IReadOnlyList<string> Tokens() => [.. Get().Devices.Select(d => d.Token)];

    /// <summary>A device registered, or registered again: a token already known keeps its id and its first date.</summary>
    public NotificationDevice Register(RegisterDeviceRequest request)
    {
        var token = request.Token.Trim();
        if (token.Length == 0)
        {
            throw new ArgumentException("A device registers with its Firebase token.");
        }

        lock (_gate)
        {
            var now = time.GetUtcNow();
            var devices = Get().Devices.ToList();
            var known = devices.FindIndex(d => d.Token == token);
            var device = known >= 0
                ? devices[known] with { Name = request.Name, Platform = request.Platform, LastSeen = now }
                : new Device(Guid.NewGuid().ToString("N")[..12], request.Name, request.Platform, token, now, now);
            if (known >= 0)
            {
                devices[known] = device;
            }
            else
            {
                devices.Add(device);
            }

            Set(new Saved(devices));
            return device.Shown();
        }
    }

    /// <summary>A device no longer sent to; false when there was none with that id.</summary>
    public bool Remove(string id) => Drop(d => d.Id == id);

    /// <summary>A token Firebase no longer knows: the app was uninstalled, or its data wiped.</summary>
    public void Forget(string token) => Drop(d => d.Token == token);

    private bool Drop(Predicate<Device> which)
    {
        lock (_gate)
        {
            var devices = Get().Devices.ToList();
            if (devices.RemoveAll(which) == 0)
            {
                return false;
            }

            Set(new Saved(devices));
            return true;
        }
    }

    /// <summary>The file: every device with its token.</summary>
    public sealed record Saved(IReadOnlyList<Device> Devices);

    /// <summary>A device as kept, its token with it.</summary>
    public sealed record Device(string Id, string Name, string Platform, string Token, DateTimeOffset Registered, DateTimeOffset LastSeen)
    {
        /// <summary>As the agent shows it: without its token.</summary>
        public NotificationDevice Shown() => new(Id, Name, Platform, Registered, LastSeen);
    }
}
