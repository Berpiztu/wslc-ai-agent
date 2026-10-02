using System.Diagnostics;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Images;
using WslcAgent.Server.Notifications;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// A container moved to another version of its image: the image pulled as the
/// Images page pulls (its console, its cancel), then the container recreated with
/// everything else it has — ports, mounts, environment, name, policy — and started
/// if it was running. The recreate is the View &amp; edit one, so a version that does
/// not start leaves the previous container in place. Started here and returned at
/// once: the agent owns the rest, and its end is a notification.
/// </summary>
public sealed class ContainerImageUpdates(IContainerService containers, ImagePulls pulls, Notifier notifier, ILogger<ContainerImageUpdates> logger)
{
    private static readonly TimeSpan PullTimeout = TimeSpan.FromHours(1);
    private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(500);
    private const string Job = "Image update";

    /// <summary>The pull started (or joined) for the new image; 404 for a container that is not there.</summary>
    public async Task<ImagePullState> StartAsync(string container, UpdateContainerImageRequest request, CancellationToken cancellationToken = default)
    {
        var details = await containers.DetailsAsync(WslcArgs.Require(container, "container"), cancellationToken);
        var image = ImageReference.Normalize(WslcArgs.Require(request.Image, "image"));
        var pull = pulls.Start(image);
        _ = Task.Run(() => FinishAsync(details.Id, details.Name, image));
        return pull;
    }

    private async Task FinishAsync(string id, string name, string image)
    {
        var elapsed = Stopwatch.StartNew();
        var error = await PulledAsync(image);
        if (error is null)
        {
            try
            {
                // Read again after the pull, which may have taken minutes: the recreate
                // keeps what the container is now, not what it was when asked.
                var details = await containers.DetailsAsync(id);
                await containers.RecreateAsync(id, details.Form with { Image = image, Start = details.IsRunning });
            }
            catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException or ArgumentException or InvalidOperationException)
            {
                error = ex.Message;
            }
        }

        if (error is not null)
        {
            logger.LogError("image update of {Container} to {Image} failed: {Error}", name, image, error);
        }

        notifier.JobEnded(Job, $"{name} → {image}", error, elapsed.Elapsed, NotificationLink.Containers, CliTraceDescription.Containers);
    }

    /// <summary>Follows the pull to its end: null when the image is there, else why not.</summary>
    private async Task<string?> PulledAsync(string image)
    {
        var deadline = DateTimeOffset.UtcNow + PullTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            switch (pulls.Get(image))
            {
                case { State: "success" }:
                    return null;
                case { State: "cancelled" }:
                    return "Pull cancelled";
                case { State: "error" } failed:
                    return failed.Error.Length > 0 ? failed.Error : "Pull failed";
            }

            await Task.Delay(PollEvery);
        }

        return "Pull timed out";
    }
}
