using Microsoft.AspNetCore.Http.HttpResults;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Host;
using WslcAgent.Server.Projects;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/projects</c>: Compose files, opened on the agent's desktop, read into a plan and brought up as projects, and the verbs on a project that is up. See docs/api-v1.md.</summary>
public static class ProjectEndpoints
{
    /// <summary>The kinds Windows' Open dialog lists, name and pattern in turn.</summary>
    private const string ComposeKinds = "Compose files (*.yaml;*.yml)|*.yaml;*.yml|All files (*.*)|*.*";

    public static RouteGroupBuilder MapProjectEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/projects");

        // A read, asked with POST: the file's text can be the body.
        group.MapPost("/plan", (ComposePlanRequest request, IProjectService projects, CancellationToken ct) => projects.PlanAsync(request, ct))
            .WithName("PlanProject");

        // What a save would do, for the question asked before it.
        group.MapPost("/preview", (ProjectUpRequest request, ProjectRunner runner, CancellationToken ct) => runner.PreviewAsync(request, ct))
            .WithName("PreviewProject");

        // The save as a job of the agent: it answers at once, and goes on with the window closed.
        group.MapPost("/up", (ProjectUpRequest request, ProjectRunner runner, CancellationToken ct) => runner.StartAsync(request, ct))
            .WithName("UpProject");

        group.MapGet("/jobs/{id}", (string id, ProjectRunner runner) => runner.Get(id))
            .WithName("ProjectJob");

        // The Compose file kept for a project: what its row opens.
        group.MapGet("/{name}", (string name, ProjectLifecycle lifecycle) => lifecycle.Stored(name))
            .WithName("StoredProject");

        // Its containers in the order its services start in: for a client that goes through them itself.
        group.MapGet("/{name}/containers", (string name, ProjectLifecycle lifecycle, CancellationToken ct) => lifecycle.ContainersAsync(name, ct))
            .WithName("ProjectContainers");

        // The verbs on the whole project, its containers taken in the order its services start in.
        group.MapPost("/{name}/start", (string name, ProjectLifecycle lifecycle, CancellationToken ct) => lifecycle.VerbAsync(name, ProjectLifecycle.Start, ct))
            .WithName("StartProject");

        group.MapPost("/{name}/stop", (string name, ProjectLifecycle lifecycle, CancellationToken ct) => lifecycle.VerbAsync(name, ProjectLifecycle.Stop, ct))
            .WithName("StopProject");

        group.MapPost("/{name}/restart", (string name, ProjectLifecycle lifecycle, CancellationToken ct) => lifecycle.VerbAsync(name, ProjectLifecycle.Restart, ct))
            .WithName("RestartProject");

        // Removes the project's containers and networks; its volumes only when the body says so.
        group.MapPost("/{name}/down", (string name, ProjectDownRequest? request, ProjectLifecycle lifecycle, CancellationToken ct) =>
                lifecycle.DownAsync(name, request?.Volumes ?? false, ct))
            .WithName("DownProject");

        // Windows' own Open dialog on the agent's desktop: only for someone sitting
        // at it. It is the one way a file comes with its path, which a browser never
        // gives; cancelled, the answer carries no path.
        group.MapPost("/open", async Task<Results<Ok<ComposeFileOpened>, ProblemHttpResult>> (HttpContext http, OpenComposeFileRequest? request) =>
            {
                if (!OperatingSystem.IsWindows() || !LocalClient.IsLocal(http))
                {
                    return TypedResults.Problem(
                        "The Open dialog shows on the server desktop and can only be requested from the server itself. Load the file from this device instead.",
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Server-local action");
                }

                var path = await HostFileDialog.OpenAsync("Open Compose file", ComposeKinds, request?.Folder ?? "");
                return TypedResults.Ok(path is null
                    ? new ComposeFileOpened("", "")
                    : new ComposeFileOpened(path, await File.ReadAllTextAsync(path, http.RequestAborted)));
            })
            .WithName("OpenProjectFile");

        return api;
    }
}
