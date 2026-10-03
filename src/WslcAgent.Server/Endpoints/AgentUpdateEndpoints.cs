using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Updates;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/agent/update</c>: Settings → Agent update, and the countdown every client shows. See docs/api-v1.md.</summary>
public static class AgentUpdateEndpoints
{
    public static RouteGroupBuilder MapAgentUpdateEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/agent/update");

        group.MapGet("", (AgentUpdater updater) => updater.Status())
            .WithName("GetAgentUpdate");

        group.MapPut("/settings", (AgentUpdateSettings settings, AgentUpdater updater) => updater.Save(settings))
            .WithName("SetAgentUpdateSettings");

        // Update now: 409 with the reason when there is nothing newer to install.
        group.MapPost("", (AgentUpdater updater) => updater.Request())
            .WithName("UpdateAgent");

        group.MapPost("/cancel", (AgentUpdater updater) => updater.Cancel())
            .WithName("CancelAgentUpdate");

        // The latest release on GitHub; 502 with the reason when GitHub does not answer.
        group.MapGet("/latest", async Task<Results<Ok<LatestRelease>, ProblemHttpResult>> (GitHubReleases releases, CancellationToken ct) =>
            {
                try
                {
                    return TypedResults.Ok(await releases.LatestAsync(ct));
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException)
                {
                    return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "GitHub did not answer");
                }
            })
            .WithName("GetLatestAgentRelease");

        // Install from GitHub: the README's line, run on the agent's machine in a window of its own.
        group.MapPost("/from-github", (GitHubReleases releases) =>
            {
                releases.RunInstallLine();
                return TypedResults.NoContent();
            })
            .WithName("InstallAgentFromGitHub");

        return api;
    }
}
