using MudBlazor;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// View &amp; edit's Save, said on the title bar's activity line from the
/// moment it is asked to its end, as a project's save is
/// (<see cref="ProjectActivity"/>): the form closes at once, the container's
/// row says it is being recreated, and the agent finishes the recreate
/// whatever happens to this window. What it ends with is said here: done,
/// with what the recreate or the image check had to say; or, refused or
/// failed, in a window of its own, since the save the user asked for did not
/// happen. The agent's log has it either way.
/// </summary>
public sealed class RecreateActivity(WslcAgentApi api, ActivityLine line, IDialogService dialogs)
{
    /// <summary>Starts the recreate of <paramref name="container"/> with the form's settings, and follows it to its end.</summary>
    public void Start(string container, string name, ContainerLaunchRequest request)
    {
        var work = line.Begin($"Recreating {name}: the image is checked and the new settings rehearsed, then the container is replaced");
        _ = RunAsync();

        async Task RunAsync()
        {
            ContainerCreated created;
            try
            {
                created = await api.RecreateContainerAsync(container, request);
            }
            catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
            {
                work.Fail($"{name} was not recreated: {ex.Message}");
                await DialogFlow.ExplainAsync(dialogs, "Recreate container", $"{name} was not recreated.\n\n{ex.Message}",
                    "It is written in the agent's log too (Logs). View & edit opens the settings again to change them.");
                return;
            }

            // With what the recreate had to adjust in the info tone; made from an image with known
            // vulnerabilities, in the warning one, kept until it is closed: it is not clean.
            if (created.Notes.Count == 0)
            {
                work.Done($"Recreated {name}");
            }
            else if (created.ImageWarnings is { Count: > 0 })
            {
                work.Quiet();
                line.Warn($"Recreated {name} from an image with known vulnerabilities (its Logs end with them). {string.Join(" ", created.Notes)}");
            }
            else
            {
                work.Quiet();
                line.Say($"Recreated {name}. {string.Join(" ", created.Notes)}");
            }
        }
    }
}
