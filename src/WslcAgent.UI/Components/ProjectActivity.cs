using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// A project's Save or Run, said on the title bar's activity line from the
/// moment it is asked to its end, as a pull is (<see cref="PullActivity"/>):
/// "Saving project shop", then the step the agent is at, then that it is
/// saved, or where it stopped and why. The window that asked for it closes at
/// once: the agent does the work, the Containers table shows each container
/// on its way, and this line says how the whole goes, wherever the user has
/// gone meanwhile.
/// </summary>
public sealed class ProjectActivity(WslcAgentApi api, ActivityLine line)
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(1);

    /// <summary>Starts the save and says so; one the agent refuses is the caller's to show (the window keeps it).</summary>
    public async Task StartAsync(ProjectUpRequest request, string name)
    {
        var work = line.Begin($"{(request.Start ? "Starting" : "Saving")} project {name}");
        ProjectJob job;
        try
        {
            job = await api.StartProjectAsync(request);
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            work.Quiet();
            throw;
        }

        _ = FollowAsync(job, work, request.Start);
    }

    /// <summary>Asks how the job goes every second until it is over; a job the agent no longer knows (it was restarted) is over too.</summary>
    private async Task FollowAsync(ProjectJob job, ActivityWork work, bool start)
    {
        // Whatever ends the following, the line is left.
        using var following = work;
        while (true)
        {
            await Task.Delay(Every);
            try
            {
                job = await api.GetProjectJobAsync(job.Id);
            }
            catch (AgentApiException ex) when (ex.StatusCode == 404)
            {
                // A second ago it was running: the agent was restarted, or its row was cancelled and dismissed meanwhile.
                work.Fail($"Project {job.Name}: the agent no longer follows it. The Containers table says how far it got.");
                return;
            }
            catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
            {
                // The agent did not answer this second (the layout says so); the next asks again.
                continue;
            }

            if (job.State == ProjectJob.Running)
            {
                work.Set($"Project {job.Name}: {job.Step}");
            }
            else if (job.State == ProjectJob.Failed)
            {
                work.Fail($"Project {job.Name} stopped at '{job.Step}'. {job.Error} Its row in Containers has the log.");
                return;
            }
            else if (job.State == ProjectJob.Cancelled)
            {
                work.Done($"Project {job.Name} cancelled at '{job.Step}'");
                return;
            }
            else
            {
                work.Done($"Project {job.Name} {(start ? "started" : "saved")}");
                return;
            }
        }
    }
}
