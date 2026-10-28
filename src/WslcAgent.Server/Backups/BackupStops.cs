using System.Runtime.ExceptionServices;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Backups;

/// <summary>
/// The running containers a backup has to stop, and what it does with them as
/// its settings say (<see cref="BackupSettings.StopOf"/>): asked, or the user
/// agreed (<see cref="BackupVolumeRequest.Stop"/>), or Yes, they are stopped,
/// the volumes held first so that nothing starts them again meanwhile
/// (<see cref="BusyVolumes"/>), and started again when the backup ends, well or
/// not, each once what it depends on runs; No, or not agreed, the backup is
/// refused while they run. A restore stops nothing: it is refused while they
/// run.
/// </summary>
public sealed class BackupStops(IContainerService containers, IVolumeService volumes, BusyVolumes busy)
{
    /// <summary>The running containers that use <paramref name="of"/>, by name, each once.</summary>
    public async Task<List<string>> RunningUsersAsync(IEnumerable<string> of, CancellationToken cancellationToken)
    {
        var running = new List<string>();
        foreach (var volume in of)
        {
            running.AddRange((await volumes.UsersAsync(volume, cancellationToken)).Containers
                .Where(user => user.State == "running")
                .Select(user => user.Name.Length > 0 ? user.Name : user.Id));
        }

        return [.. running.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// <paramref name="work"/>, the backup of <paramref name="name"/>, done
    /// with <paramref name="running"/> stopped, as <paramref name="stop"/>
    /// says, or the user agreed; <paramref name="held"/> held from before they
    /// stop until before they start again. Those it stopped are started again
    /// whatever the backup did; one that does not start fails a backup that
    /// went well, which is written, and is said in the log of one that did not.
    /// </summary>
    public async Task StoppedAsync(BackupWork job, string name, IReadOnlyList<string> held, IReadOnlyList<string> running,
        string stop, bool agreed, Func<Task> work, CancellationToken cancellationToken)
    {
        if (running.Count == 0)
        {
            await work();
            return;
        }

        var they = running.Count == 1 ? "it" : "them";
        var said = $"{string.Join(", ", running)} {(running.Count == 1 ? "is" : "are")} running, and {name} is backed up with {they} stopped";
        if (stop == BackupChoice.No)
        {
            throw new InvalidOperationException($"{said}; its backup settings say not to stop {they}: stop {they} first, then back it up.");
        }

        if (stop != BackupChoice.Yes && !agreed)
        {
            throw new InvalidOperationException($"{said}: stop {they} first, or back it up agreeing to stop {they}.");
        }

        var stopped = new List<string>();
        Exception? failed = null;
        var holds = held.Select(volume => busy.Hold(volume, "backed up")).ToList();
        try
        {
            await StopAllAsync(job, running, stopped, cancellationToken);
            await work();
        }
        catch (Exception ex)
        {
            failed = ex;
        }
        finally
        {
            holds.ForEach(hold => hold.Dispose());
        }

        var notStarted = await StartAgainAsync(job, stopped);
        if (failed is not null)
        {
            ExceptionDispatchInfo.Throw(failed);
        }

        if (notStarted.Count > 0)
        {
            throw new InvalidOperationException($"The backup is written, but {string.Join(", ", notStarted)} {(notStarted.Count == 1 ? "was" : "were")} not started again: start {(notStarted.Count == 1 ? "it" : "them")} by hand.");
        }
    }

    /// <summary>
    /// <paramref name="running"/> stopped, each added to <paramref name="stopped"/>
    /// as it stops: one another of them depends on once that one has stopped,
    /// so the rounds go on while one stops. One that cannot be stopped (another
    /// container, not in the backup, depends on it) fails the backup, saying why.
    /// </summary>
    private async Task StopAllAsync(BackupWork job, IReadOnlyList<string> running, List<string> stopped, CancellationToken cancellationToken)
    {
        var waiting = new List<string>(running);
        var why = new Dictionary<string, string>(StringComparer.Ordinal);
        bool moved;
        do
        {
            moved = false;
            foreach (var container in waiting.ToList())
            {
                job.Step($"Stopping {container}");
                try
                {
                    await containers.StopAsync(container, cancellationToken);
                    stopped.Add(container);
                    waiting.Remove(container);
                    why.Remove(container);
                    moved = true;
                    job.Say($"INFO: {container} stopped for the backup; it is started again when the backup ends.");
                }
                // A container that depends on it still runs: it may be one of these, stopped later in the round.
                catch (InvalidOperationException ex)
                {
                    why[container] = ex.Message;
                }
            }
        }
        while (moved && waiting.Count > 0);

        if (why.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", why.Values));
        }
    }

    /// <summary>
    /// Those the backup stopped, started again, never cancelled: each once what
    /// it depends on runs, so the rounds go on while one starts. Returns those
    /// that did not, each said in the log with why.
    /// </summary>
    private async Task<List<string>> StartAgainAsync(BackupWork job, List<string> stopped)
    {
        var waiting = new List<string>(stopped);
        var why = new Dictionary<string, string>(StringComparer.Ordinal);
        bool moved;
        do
        {
            moved = false;
            foreach (var container in waiting.ToList())
            {
                job.Step($"Starting {container} again");
                try
                {
                    await containers.StartAsync(container, CancellationToken.None);
                    waiting.Remove(container);
                    why.Remove(container);
                    moved = true;
                    job.Say($"INFO: {container} started again.");
                }
                catch (Exception ex) when (ex is InvalidOperationException or WslcException or WslcNotFoundException or TimeoutException)
                {
                    why[container] = ex.Message;
                }
            }
        }
        while (moved && waiting.Count > 0);

        foreach (var (container, reason) in why)
        {
            job.Say($"WARNING: {container} was not started again: {reason}");
        }

        return [.. why.Keys];
    }
}
