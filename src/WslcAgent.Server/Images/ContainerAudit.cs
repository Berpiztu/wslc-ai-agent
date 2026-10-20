using System.Collections.Concurrent;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// The image of every running container the agent sees with no mark on it
/// yet (<see cref="ImageMarks"/>) is checked once, after the fact
/// (<see cref="ImageSecurity.MarkAsync"/>): <c>wslc</c> can be run around the
/// agent, and a container of a vulnerable image is as vulnerable whoever made
/// it. A stopped container is not: one stopped for a month is checked when it
/// is started again, when the list first shows it running, not while it sits
/// there. Nothing is stopped; what is found marks the image, which its
/// containers read, and every client is told to read the list again. One
/// image at a time, in the background, so the list never waits on it; each
/// once while the agent runs, its libraries read once by the image's id.
/// </summary>
public sealed class ContainerAudit(ImageSecurity security, ImageMarks marks, WslcEvents events, ILogger<ContainerAudit> logger)
{
    /// <summary>The images checked already while the agent runs, by name.</summary>
    private readonly ConcurrentDictionary<string, bool> _seen = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentQueue<string> _waiting = new();
    private int _running;

    /// <summary>The images of a list's running containers not checked yet, queued; the check goes on in the background.</summary>
    public void Schedule(IReadOnlyList<ContainerSummary> containers)
    {
        foreach (var image in containers
            .Where(container => container.IsRunning && !container.IsRecreating && container.Image.Trim().Length > 0)
            .Select(container => container.Image.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(image => marks.Of(image) is null && _seen.TryAdd(image, true)))
        {
            _waiting.Enqueue(image);
        }

        if (!_waiting.IsEmpty && Interlocked.CompareExchange(ref _running, 1, 0) == 0)
        {
            _ = Task.Run(DrainAsync);
        }
    }

    private async Task DrainAsync()
    {
        try
        {
            while (_waiting.TryDequeue(out var image))
            {
                try
                {
                    await security.MarkAsync(image, CancellationToken.None);
                    if (marks.Of(image) is not null)
                    {
                        events.Publish(new ChangeNotice([ChangeNotice.Container]));
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // One image that cannot be checked says nothing of the next.
                    logger.LogWarning("image security check of image {Image} could not be made: {Message}", image, ex.Message);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
            if (!_waiting.IsEmpty && Interlocked.CompareExchange(ref _running, 1, 0) == 0)
            {
                _ = Task.Run(DrainAsync);
            }
        }
    }
}
