using MudBlazor;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// Zaindari's patch, said on the title bar's activity line from the moment it
/// is asked to its end, as a recreate is (<see cref="RecreateActivity"/>): the
/// window closes at once, and the line says the patch is being built and the
/// step it is at (<c>[2/3] RUN apt-get …</c>), so a build of half a minute is
/// never a silence. What it ends with is said here: built and the containers
/// moved; or, failed, in a window of its own, with the agent's words. The
/// agent's log has it either way, and its notification says it too.
/// </summary>
public sealed class PatchActivity(WslcAgentApi api, ActivityLine line, IDialogService dialogs)
{
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(1);

    /// <summary>Starts the patch and follows it; an <see cref="AgentApiException"/> (nothing to do, not there) is the caller's to show.</summary>
    public async Task StartAsync(PatchImageRequest request) =>
        Follow(await api.PatchImageAsync(request), request.Image);

    /// <summary>Follows a patch's build job to its end on the activity line.</summary>
    public void Follow(BuildJob job, string image)
    {
        var work = line.Begin($"Patching {image}: building {job.Tag}");
        _ = RunAsync();

        async Task RunAsync()
        {
            using var timer = new PeriodicTimer(PollEvery);
            try
            {
                while (await timer.WaitForNextTickAsync())
                {
                    var state = await api.GetImageBuildAsync(job.Job);
                    if (state.State == "running")
                    {
                        work.Set($"Patching {image}: {Step(state.Output, job.Tag)}");
                        continue;
                    }

                    var moved = state.Output.Count(output => output.Contains(" now runs ", StringComparison.Ordinal));
                    if (state.State == "done")
                    {
                        work.Done($"Patched {image}: {state.Tag} built{(moved > 0 ? $", {moved} container{(moved == 1 ? "" : "s")} moved to it" : "")}");
                    }
                    else
                    {
                        work.Fail($"The patch of {image} did not end: {state.Error}");
                        await DialogFlow.ExplainAsync(dialogs, "Patch", $"The patch of {image} did not end.\n\n{state.Error}",
                            "Its build output and the reason are in the agent's log too (Logs). A container not moved runs on as it was.");
                    }

                    return;
                }
            }
            catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
            {
                work.Fail($"The patch of {image} could not be followed: {ex.Message}. The agent goes on with it; its notification says how it ends.");
            }
        }
    }

    /// <summary>The step the build is at, its last <c>[n/m]</c> line; until there is one, that it is building.</summary>
    private static string Step(IReadOnlyList<string> output, string tag) =>
        output.LastOrDefault(text => text.TrimStart().StartsWith('[')) is { } step
            ? step.Trim().Length > 90 ? step.Trim()[..90] + "…" : step.Trim()
            : $"building {tag}";
}
