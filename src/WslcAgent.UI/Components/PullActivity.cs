using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// A pull the user asked for, said on the title bar's activity line from the
/// moment it is asked to its end: "Pulling alpine:latest…" with its
/// percentage, then "alpine:latest pulled", its failure, or its cancel. It is
/// followed here, not by the Images page, so its end is said wherever the user
/// has gone meanwhile; the page says the end of the pulls it did not start
/// (<see cref="Follows"/>).
/// </summary>
public sealed class PullActivity(WslcAgentApi api, ActivityLine line)
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(1);

    /// <summary>The agent keeps a failed pull listed this long: the page must not say it a second time meanwhile.</summary>
    private static readonly TimeSpan ListedAfterEnd = TimeSpan.FromMinutes(5);

    /// <summary>The images whose pull this follows or followed, and when it ended (null while it runs).</summary>
    private readonly Dictionary<string, DateTimeOffset?> _followed = new(StringComparer.Ordinal);

    /// <summary>This pull's end is said here: running, or ended while the agent still lists it.</summary>
    public bool Follows(string image) =>
        _followed.TryGetValue(image, out var ended) && (ended is null || DateTimeOffset.UtcNow - ended < ListedAfterEnd);

    /// <summary>Starts the pull and says so; one the agent refuses is the caller's to show (the dialog keeps it).</summary>
    public async Task StartAsync(string reference) => _ = await BeginAsync(reference, said: null);

    /// <summary>
    /// Starts the pull and waits for its end, for a caller that goes on with
    /// the image: true when it was pulled. Each state of it is handed to
    /// <paramref name="said"/> too, for a window that covers the activity line
    /// and draws the pull itself, as the lists do.
    /// </summary>
    public async Task<bool> PullAsync(string reference, Action<ImagePullState> said) => await await BeginAsync(reference, said);

    /// <summary>The pull started, and the task that follows it to its end.</summary>
    private async Task<Task<bool>> BeginAsync(string reference, Action<ImagePullState>? said)
    {
        var work = line.Begin($"Pulling {reference}");
        ImagePullState started;
        try
        {
            started = await api.StartImagePullAsync(reference, allTags: false);
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            work.Quiet();
            throw;
        }

        _followed[started.Image] = null;
        said?.Invoke(started);
        return FollowAsync(started.Image, work, said);
    }

    /// <summary>
    /// Asks for the pulls every second until this one is over. A pull is
    /// listed for 5 s once it worked, so one that took two seconds is still
    /// seen ending; one no longer listed at all ended well.
    /// </summary>
    private async Task<bool> FollowAsync(string image, ActivityWork work, Action<ImagePullState>? said)
    {
        // Whatever ends the following, the line is left.
        using var following = work;
        while (true)
        {
            await Task.Delay(Every);
            ImagePullState? pull;
            try
            {
                pull = (await api.GetImagePullsAsync()).FirstOrDefault(p => p.Image == image);
            }
            catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
            {
                // The agent did not answer this second (the layout says so); the next asks again.
                continue;
            }

            switch (pull)
            {
                case { State: "running" }:
                    work.Set(pull.Pct > 0 ? $"Pulling {image} {pull.Pct}%" : $"Pulling {image}");
                    said?.Invoke(pull);
                    continue;
                case { State: "error" }:
                    work.Fail($"Pull of {image} failed: {(pull.Error.Length > 0 ? pull.Error : pull.Status)}");
                    break;
                case { State: "cancelled" }:
                    work.Quiet();
                    line.Say($"Pull of {image} cancelled");
                    break;
                default:
                    work.Done($"{image} pulled");
                    break;
            }

            // One no longer listed ended well, and has no state left to hand over.
            if (pull is not null)
            {
                said?.Invoke(pull);
            }

            _followed[image] = DateTimeOffset.UtcNow;
            return pull is null or { State: not ("error" or "cancelled") };
        }
    }
}
