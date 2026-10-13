using Microsoft.JSInterop;
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

    /// <summary>
    /// Starts a save that has something to do first, on this device: sending
    /// what the project's images are built from. All of it is done with the
    /// window closed, as the save itself is: a folder of ten thousand files
    /// over a slow line is not something to sit and watch. The line says how
    /// far <paramref name="before"/> has got, then the save's own steps; a
    /// failure of either stays on it until it is closed.
    /// </summary>
    public void StartAfter(Func<Action<string>, Task> before, ProjectUpRequest request, string name)
    {
        var work = line.Begin($"Project {name}: sending what its images are built from");
        _ = RunAsync();

        async Task RunAsync()
        {
            ProjectJob job;
            try
            {
                await before(step => work.Set($"Project {name}: {step}"));
                work.Set($"{(request.Start ? "Starting" : "Saving")} project {name}");
                job = await api.StartProjectAsync(request);
            }
            catch (Exception ex) when (ex is AgentApiException or HttpRequestException or JSException or InvalidOperationException)
            {
                work.Fail($"Project {name} was not {(request.Start ? "started" : "saved")}: {ex.Message}");
                return;
            }

            await FollowAsync(job, work, request.Start);
        }
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
