using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Containers;
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

        // The images the file's services are built from, and nothing else: a job as the save is.
        group.MapPost("/build", (ProjectBuildRequest request, ProjectRunner runner, CancellationToken ct) => runner.StartBuildAsync(request, ct))
            .WithName("BuildProject");

        group.MapGet("/jobs/{id}", (string id, ProjectRunner runner) => runner.Get(id))
            .WithName("ProjectJob");

        // The last save or build of a project, kept once it ends: the one that went well too.
        group.MapGet("/{name}/last-job", (string name, ProjectRunner runner) => runner.LastJob(name))
            .WithName("ProjectLastJob");

        // A build context from a client on another machine, in three steps, so that only what
        // the agent lacks travels. Which of the client's files, by their hashes, are not here.
        group.MapPost("/contexts/files/missing", (ContextFiles files, ProjectContexts contexts) => new ContextFiles(contexts.Missing(files.Hashes ?? [])))
            .WithName("MissingContextFiles");

        // Those files, as a tar archive whose entries are named by their hashes. A folder of
        // sources runs to gigabytes: no size limit. It is a transfer of the agent's, as a file
        // into a container is: every client sees it in the list with how far it has got, its
        // cross stops it, and the agent does not update itself under it. Its place is the
        // project's, which has no container yet to stand under.
        group.MapPost("/contexts/files", async Task<Results<NoContent, ProblemHttpResult>> (
                HttpRequest request, ProjectContexts contexts, ContainerTransfers transfers,
                string project = "", string context = ".", int files = 0, CancellationToken ct = default) =>
            {
                var (_, body) = await ContainerEndpoints.FileSectionAsync(request, ct);
                using var upload = transfers.Start(
                    project.Trim().Length > 0 ? project.Trim() : "Compose project", ContainerTransfer.In, $"build context {context}",
                    // One archive, said as one: with the count alone the row read as that many transfers.
                    files > 0 ? $"one tar archive ({files} files)" : "one tar archive", request.ContentLength ?? 0, ct);
                try
                {
                    await contexts.SaveFilesAsync(body, upload.Progress, upload.Token);
                    upload.Finish();
                    return TypedResults.NoContent();
                }
                catch (OperationCanceledException)
                {
                    // The cross on its row stopped it, or the client that was sending it went:
                    // an answer, not a failure.
                    upload.Fail("Upload cancelled");
                    return TypedResults.Problem("The upload was cancelled.", statusCode: StatusCodes.Status409Conflict, title: "Upload cancelled");
                }
                catch (Exception exception)
                {
                    // The row says why, whatever the client that started it does with the answer.
                    upload.Fail(exception.Message);
                    throw;
                }
            })
            .WithName("SendContextFiles")
            .DisableAntiforgery()
            .WithMetadata(new DisableRequestSizeLimitAttribute());

        // And the context itself, as the list of what is in it: kept, and named by the client
        // in the file's request.
        group.MapPost("/contexts", (ProjectContextRequest context, ProjectContexts contexts) => contexts.Save(context))
            .WithName("SendProjectContext");

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

        // The project's logs as one, each line said by its container: a whole reading, or the lines after one already read.
        group.MapGet("/{name}/logs", (string name, ProjectLifecycle lifecycle, CancellationToken ct, int tail = 200, string since = "", string services = "") =>
                lifecycle.LogsAsync(name, tail, since, services.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), ct))
            .WithName("ProjectLogs");

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
