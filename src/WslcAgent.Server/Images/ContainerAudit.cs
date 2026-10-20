using System.Collections.Concurrent;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// Every container the agent sees and did not make, or made before the image
/// check was, has its image checked once, after the fact
/// (<see cref="ImageSecurity.AuditAsync"/>): <c>wslc</c> can be run around
/// the agent, and a container of a vulnerable image is as vulnerable whoever
/// made it. Nothing is stopped; what is found marks the container as the
/// check marks one it let be made (<see cref="ImageWarnings"/>), and every
/// client is told to read the list again. One container at a time, in the
/// background, so the list never waits on it; each once while the agent
/// runs, its image's libraries read once by the image's id.
/// </summary>
public sealed class ContainerAudit(ImageSecurity security, ImageWarnings warnings, WslcEvents events, ILogger<ContainerAudit> logger)
{
    /// <summary>The containers checked already, or made by the agent and checked as they were made, by id.</summary>
    private readonly ConcurrentDictionary<string, bool> _seen = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentQueue<ContainerSummary> _waiting = new();
    private int _running;

    /// <summary>A container the agent has just made, its image checked as it was: not to be checked again.</summary>
    public void Seen(string id)
    {
        if (id.Trim().Length > 0)
        {
            _seen[id.Trim()] = true;
        }
    }

    /// <summary>The containers of a list not checked yet, queued; the check goes on in the background.</summary>
    public void Schedule(IReadOnlyList<ContainerSummary> containers)
    {
        // One already marked was found as it was made, or at an earlier start of the agent; a mark
        // kept before the worst severity was is made again, so its chip says how bad.
        foreach (var container in containers.Where(container => !container.IsRecreating && (!container.HasImageWarning || container.ImageWorst.Length == 0)
            && container.Image.Length > 0 && !Known(container.Id)))
        {
            _seen[container.Id] = true;
            _waiting.Enqueue(container);
        }

        if (!_waiting.IsEmpty && Interlocked.CompareExchange(ref _running, 1, 0) == 0)
        {
            _ = Task.Run(DrainAsync);
        }
    }

    /// <summary>A short id and a full one alike: the list gives the short, a launch the full.</summary>
    private bool Known(string id) =>
        _seen.ContainsKey(id) || _seen.Keys.Any(seen => seen.StartsWith(id, StringComparison.OrdinalIgnoreCase) || id.StartsWith(seen, StringComparison.OrdinalIgnoreCase));

    private async Task DrainAsync()
    {
        try
        {
            while (_waiting.TryDequeue(out var container))
            {
                try
                {
                    var (said, refused, worst) = await security.AuditAsync(container.Image, container.Name, CancellationToken.None);
                    if (said.Count > 0)
                    {
                        warnings.Remember(container.Id, container.Name, said, refused, worst);
                        events.Publish(new ChangeNotice([ChangeNotice.Container]));
                    }
                    else if (container.HasImageWarning)
                    {
                        warnings.Forget(container.Id);
                        events.Publish(new ChangeNotice([ChangeNotice.Container]));
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // One container that cannot be checked says nothing of the next.
                    logger.LogWarning("image security check of container {Container} could not be made: {Message}", container.Name, ex.Message);
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
