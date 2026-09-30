using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Host;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/containers</c>: list, lifecycle, create/run/recreate, details, logs, stats, inspect JSON, backups, restart policy. See docs/api-v1.md.</summary>
public static class ContainerEndpoints
{
    public static RouteGroupBuilder MapContainerEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/containers");

        group.MapGet("", async Task<Ok<ContainerListResponse>> (IContainerService containers, bool all = true, bool helpers = false, CancellationToken ct = default) =>
                TypedResults.Ok(await containers.ListAsync(all, helpers, ct)))
            .WithName("ListContainers");

        group.MapPost("", (ContainerLaunchRequest request, IContainerService containers, CancellationToken ct) => containers.CreateAsync(request, ct))
            .WithName("CreateContainer");

        group.MapPost("/run", (ContainerLaunchRequest request, IContainerService containers, CancellationToken ct) => containers.RunAsync(request, ct))
            .WithName("RunContainer");

        // Before a run, create or recreate: the form against itself and against the machine (docs/api-v1.md).
        group.MapPost("/launch-check", (LaunchCheckRequest body, ILaunchChecks checks, CancellationToken ct) => checks.CheckAsync(body.Request, body.Source, ct))
            .WithName("CheckContainerLaunch");

        // Run as a job the agent owns: the image is pulled first when it is not local.
        group.MapGet("/launches", (ContainerLaunches launches) => launches.List())
            .WithName("ContainerLaunches");

        group.MapPost("/launches", (ContainerLaunchRequest request, ContainerLaunches launches) => launches.Enqueue(request))
            .WithName("LaunchContainer");

        group.MapPost("/launches/{id}/cancel", NoContent (string id, ContainerLaunches launches) =>
            {
                launches.Cancel(id);
                return TypedResults.NoContent();
            })
            .WithName("CancelContainerLaunch");

        // The failed row's Edit: the form opens on what the run was started with.
        group.MapGet("/launches/{id}/request", (string id, ContainerLaunches launches) => launches.Request(id))
            .WithName("ContainerLaunchRequest");

        // A failed run is remembered until dismissed, a cancelled one a while; the row's cross forgets it now (409 while it still runs).
        group.MapDelete("/launches/{id}", NoContent (string id, ContainerLaunches launches) =>
            {
                launches.Dismiss(id);
                return TypedResults.NoContent();
            })
            .WithName("DismissContainerLaunch");

        group.MapPost("/{container}/recreate", (string container, ContainerLaunchRequest request, IContainerService containers, CancellationToken ct) =>
                containers.RecreateAsync(container, request, ct))
            .WithName("RecreateContainer");

        MapLifecycle(group, "start", (s, id, ct) => s.StartAsync(id, ct));
        MapLifecycle(group, "stop", (s, id, ct) => s.StopAsync(id, ct));
        MapLifecycle(group, "restart", (s, id, ct) => s.RestartAsync(id, ct));
        MapLifecycle(group, "kill", (s, id, ct) => s.KillAsync(id, ct));

        group.MapGet("/{container}/details", (string container, IContainerService containers, CancellationToken ct) => containers.DetailsAsync(container, ct))
            .WithName("ContainerDetails");

        group.MapGet("/{container}/logs", (string container, IContainerService containers, int tail = 200, bool timestamps = false, CancellationToken ct = default) =>
                containers.LogsAsync(container, tail, timestamps, ct))
            .WithName("ContainerLogs");

        group.MapGet("/{container}/stats", (string container, IContainerService containers, CancellationToken ct) => containers.StatsAsync(container, ct))
            .WithName("ContainerStats");

        // Export JSON: the inspect output as a file the browser saves.
        group.MapGet("/{container}/inspect.json", async (string container, IContainerService containers, CancellationToken ct) =>
            {
                var (name, json) = await containers.InspectJsonAsync(container, ct);
                return Results.File(Encoding.UTF8.GetBytes(json), "application/json", $"{name}-inspect.json");
            })
            .WithName("ContainerInspectJson");

        // Load JSON file: the form read from a JSON the user picked.
        group.MapPost("/launch-form", (LaunchFormSource source, IContainerService containers) => containers.LaunchFormFromJson(source.Json))
            .WithName("LaunchFormFromJson");

        MapExec(group);
        MapFiles(group);
        MapBackups(group);

        group.MapGet("/{container}/restart-policy", (string container, IContainerService containers) => containers.GetRestartPolicy(container))
            .WithName("GetRestartPolicy");

        group.MapPut("/{container}/restart-policy", (string container, SetRestartPolicyRequest request, IContainerService containers, CancellationToken ct) =>
                containers.SetRestartPolicyAsync(container, request.Policy, ct))
            .WithName("SetRestartPolicy");

        group.MapDelete("/{container}", async Task<NoContent> (string container, IContainerService containers, bool force = false, CancellationToken ct = default) =>
            {
                await containers.RemoveAsync(container, force, ct);
                return TypedResults.NoContent();
            })
            .WithName("RemoveContainer");

        return api;
    }

    /// <summary>
    /// Running things inside a container: one command, the interactive terminal
    /// over a WebSocket, and a terminal window on the agent's own desktop.
    /// </summary>
    private static void MapExec(RouteGroupBuilder group)
    {
        group.MapPost("/{container}/exec", (string container, ContainerExecRequest request, IContainerService containers, CancellationToken ct) =>
                containers.ExecAsync(container, request.Command, ct))
            .WithName("ExecInContainer");

        // The interactive shell: the protocol is ExecTerminals'.
        group.Map("/{container}/exec/stream", async (HttpContext http, string container, ExecTerminals terminals) =>
            {
                if (!http.WebSockets.IsWebSocketRequest)
                {
                    return Results.Problem("This endpoint is a WebSocket.", statusCode: StatusCodes.Status400BadRequest, title: "Not a WebSocket request");
                }

                using var socket = await http.WebSockets.AcceptWebSocketAsync();
                await terminals.RunAsync(socket, container, http.RequestAborted);
                return Results.Empty;
            })
            .WithName("ContainerExecStream");

        // A window on the agent's desktop: only for someone sitting at it.
        group.MapPost("/{container}/open-terminal", async Task<Results<Ok<NativeTerminalResult>, ProblemHttpResult>> (
                HttpContext http, string container, OpenTerminalRequest? request, NativeTerminals terminals, CancellationToken ct) =>
            {
                if (!LocalClient.IsLocal(http))
                {
                    return TypedResults.Problem(
                        "A terminal window opens on the agent's own desktop, so it can only be asked for from that machine. Use Exec instead.",
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Server-local action");
                }

                return TypedResults.Ok(await terminals.OpenAsync(container, request?.Command, ct));
            })
            .WithName("OpenContainerTerminal");
    }

    /// <summary>
    /// The files inside a container: browse, read and write text, move bytes in
    /// and out, and the ordinary shell verbs (mkdir, mv, cp, rm). There is no
    /// file API in <c>wslc</c>; <see cref="ContainerFiles"/> composes them.
    /// </summary>
    private static void MapFiles(RouteGroupBuilder group)
    {
        group.MapGet("/{container}/files", (string container, ContainerFiles files, string path = "/", CancellationToken ct = default) =>
                files.ListAsync(container, path, ct))
            .WithName("ListContainerFiles");

        // A download is a job like an upload: the copy out of the container is
        // watched as the staged file grows, the send to the client is counted
        // as it goes, and the container's row shows the one ring for both.
        group.MapGet("/{container}/files/download", async (string container, string path, ContainerFiles files, ContainerTransfers transfers, string? queued = null, CancellationToken ct = default) =>
            {
                var transfer = Carrying(transfers, queued, container, ContainerTransfer.Out, ContainerPath.Parent(path), ContainerPath.Name(path), 0, ct);
                try
                {
                    var (staged, name) = await files.StageForDownloadAsync(container, path, transfer, transfer.Token);
                    transfer.Sending();
                    // The stream owns the staged copy and the job: it takes the
                    // file with it when the response ends, and says there
                    // whether the last byte made it.
                    var file = new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.DeleteOnClose);
                    return Results.File(new TransferStream(file, transfer), "application/octet-stream", name);
                }
                catch (Exception exception)
                {
                    // Nothing was sent, so nothing owns the job: it ends here.
                    transfer.Fail(exception.Message);
                    transfer.Dispose();
                    throw;
                }
            })
            .WithName("DownloadContainerFile");

        // The upload is a job the agent owns while the request lasts: the Files
        // view can close and the container's row keeps the ring, the reason and
        // the cross. The request still carries the bytes, so the client has to
        // stay; what is known about them is the agent's, for every client.
        group.MapPost("/{container}/files/upload", async Task<Results<Ok<ContainerFileEntry>, ProblemHttpResult>> (
                HttpRequest request, string container, ContainerFiles files, ContainerTransfers transfers, string path = "/", long size = 0, string? queued = null, CancellationToken ct = default) =>
            {
                var (name, body) = await FileSectionAsync(request, ct);
                using var upload = Carrying(transfers, queued, container, ContainerTransfer.In, path, name, size > 0 ? size : request.ContentLength ?? 0, ct);
                try
                {
                    // What was announced is what lands: a file that took its
                    // turn goes in under the name and into the folder the queue
                    // holds for it, not under whatever the request now says.
                    var entry = await files.UploadAsync(upload.Container, upload.Directory, upload.Name, body, upload, upload.Token);
                    upload.Finish();
                    return TypedResults.Ok(entry);
                }
                catch (OperationCanceledException)
                {
                    // The cross on the row stopped it, or the client that was
                    // sending it went: an answer, not a failure, and it does not
                    // belong in the log as one.
                    upload.Fail("Upload cancelled");
                    return TypedResults.Problem("The upload was cancelled.", statusCode: StatusCodes.Status409Conflict, title: "Upload cancelled");
                }
                catch (Exception exception)
                {
                    // The row says why, whatever the client that started it does
                    // with the answer — it may not be listening any more.
                    upload.Fail(exception.Message);
                    throw;
                }
            })
            .WithName("UploadContainerFile")
            .DisableAntiforgery()
            .WithMetadata(new DisableRequestSizeLimitAttribute());

        // A client says what it is about to move before it moves any of it:
        // the files take their places at the back of
        // their direction's queue and the ids that come back are what make them
        // that client's own. Nothing travels until each is the head of its
        // queue and claims its turn on the upload or download endpoint.
        group.MapPost("/{container}/files/queue", async (string container, AnnounceTransfersRequest request,
                ContainerFiles files, ContainerTransfers transfers, CancellationToken ct) =>
            {
                var directory = ContainerPath.Clean(request.Directory);
                if (request.Direction == ContainerTransfer.In
                    && await AlreadyThereAsync(files, container, directory, request, ct) is { Count: > 0 } clashes)
                {
                    // Nothing is queued, not even the files that would have
                    // been fine: the client asks about each of these and
                    // announces the whole batch again. Half a batch queued
                    // while the user is being asked would take its turn
                    // without them.
                    return new AnnouncedTransfers("", [], clashes);
                }

                return transfers.Announce(container, request.Direction, directory, request.Files);
            })
            .WithName("AnnounceContainerTransfers");

        group.MapGet("/transfers", (ContainerTransfers transfers) => transfers.List())
            .WithName("ContainerTransfers");

        // A whole announcement let go at once: the client saying it will not
        // send those files after all. Only the ones still waiting — one already
        // travelling has its own cross, and stopping it is a different thing.
        group.MapDelete("/transfers/batch/{batch}", NoContent (string batch, ContainerTransfers transfers) =>
            {
                transfers.DropBatch(batch);
                return TypedResults.NoContent();
            })
            .WithName("DropContainerTransferBatch");

        group.MapPost("/transfers/{id}/cancel", NoContent (string id, ContainerTransfers transfers) =>
            {
                transfers.Cancel(id);
                return TypedResults.NoContent();
            })
            .WithName("CancelContainerTransfer");

        // A failed transfer is remembered until dismissed, a cancelled one a while; the row's cross forgets it now (409 while it is still on its way).
        group.MapDelete("/transfers/{id}", NoContent (string id, ContainerTransfers transfers) =>
            {
                transfers.Dismiss(id);
                return TypedResults.NoContent();
            })
            .WithName("DismissContainerTransfer");

        group.MapGet("/{container}/files/content", (string container, string path, ContainerFiles files, CancellationToken ct) =>
                files.ReadAsync(container, path, ct))
            .WithName("ReadContainerFile");

        group.MapPut("/{container}/files/content", (string container, WriteFileRequest request, ContainerFiles files, CancellationToken ct) =>
                files.WriteAsync(container, request.Path, request.Content, ct))
            .WithName("WriteContainerFile");

        group.MapDelete("/{container}/files", async Task<NoContent> (string container, string path, ContainerFiles files, CancellationToken ct) =>
            {
                await files.DeleteAsync(container, path, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteContainerFile");

        group.MapPost("/{container}/files/mkdir", (string container, MakeDirectoryRequest request, ContainerFiles files, CancellationToken ct) =>
                files.MakeDirectoryAsync(container, request.Path, ct))
            .WithName("MakeContainerDirectory");

        group.MapPost("/{container}/files/rename", (string container, MoveFileRequest request, ContainerFiles files, CancellationToken ct) =>
                files.RenameAsync(container, request.Source, request.Destination, ct))
            .WithName("RenameContainerFile");

        group.MapPost("/{container}/files/copy", (string container, MoveFileRequest request, ContainerFiles files, CancellationToken ct) =>
                files.CopyAsync(container, request.Source, request.Destination, ct))
            .WithName("CopyContainerFile");
    }

    /// <summary>The backup job: start, poll, download the archive, finish or discard.</summary>
    private static void MapBackups(RouteGroupBuilder group)
    {
        group.MapPost("/{container}/backup", (string container, IContainerBackups backups, CancellationToken ct) => backups.StartAsync(container, ct))
            .WithName("StartBackup");

        group.MapGet("/backups/{job}", (string job, IContainerBackups backups) => backups.Get(job))
            .WithName("GetBackup");

        group.MapGet("/backups/{job}/download", (string job, IContainerBackups backups) =>
            {
                var info = backups.Get(job);
                var path = backups.BeginDownload(job);
                return path is null
                    ? Results.Problem("The archive is not ready.", statusCode: StatusCodes.Status409Conflict, title: "Backup not ready")
                    : Results.File(path, "application/x-tar", info.Filename);
            })
            .WithName("DownloadBackup");

        group.MapPost("/backups/{job}/cancel", async Task<NoContent> (string job, IContainerBackups backups) =>
            {
                await backups.CancelAsync(job);
                return TypedResults.NoContent();
            })
            .WithName("CancelBackup");
    }

    /// <summary>
    /// The uploaded file as it arrives: its name, and the body still on the
    /// wire. The multipart body is read here instead of being bound to an
    /// <see cref="IFormFile"/>, which spools every byte to a temporary file
    /// before the endpoint is even called — a second copy of a file that can be
    /// half a gigabyte, and a ring that would only start moving once the
    /// upload was already over.
    /// </summary>
    /// <summary>
    /// Which of the announced names are already in that folder and have not
    /// been agreed to. The agent is what knows: a client's listing is as old
    /// as the last time it read the folder, and a file written a second ago
    /// would be overwritten without a word. Asked once for the whole batch —
    /// one listing, not one test per file — and before anything travels,
    /// because a file's turn can come long after the user has left the screen.
    /// <para>
    /// A stopped container, or a folder that is not there, fails here rather
    /// than on the first transfer, which is the better place for it.
    /// </para>
    /// </summary>
    private static async Task<IReadOnlyList<string>> AlreadyThereAsync(ContainerFiles files, string container,
        string directory, AnnounceTransfersRequest request, CancellationToken cancellationToken)
    {
        var listing = await files.ListAsync(container, directory, cancellationToken);
        var there = listing.Entries.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        var agreed = (request.Overwrite ?? []).ToHashSet(StringComparer.Ordinal);
        return [.. request.Files.Select(file => file.Name).Where(name => there.Contains(name) && !agreed.Contains(name))];
    }

    /// <summary>
    /// The job this request carries: the place the client was given when it
    /// announced the batch, claimed now that its turn has come, or one made on
    /// the spot for a client that announced nothing. Claiming is refused
    /// unless the file is the head of its queue, so a client cannot cut the
    /// line by sending early.
    /// </summary>
    private static ContainerTransfers.Transfer Carrying(ContainerTransfers transfers, string? queued, string container,
        string direction, string directory, string name, long totalBytes, CancellationToken clientGone) =>
        queued is { Length: > 0 } id
            ? transfers.Claim(id, clientGone)
            : transfers.Start(container, direction, directory, name, totalBytes, clientGone);

    private static async Task<(string Name, Stream Body)> FileSectionAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var boundary = MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            ? HeaderUtilities.RemoveQuotes(contentType.Boundary).Value
            : null;
        if (boundary is not { Length: > 0 })
        {
            throw new ArgumentException("An upload is a multipart form with the file in it.");
        }

        var reader = new MultipartReader(boundary, request.Body);
        while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
            {
                continue;
            }

            // The starred form first: it is the one that survives a name with accents.
            var filename = disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName;
            if (HeaderUtilities.RemoveQuotes(filename).Value is { Length: > 0 } name)
            {
                return (name, section.Body);
            }
        }

        throw new ArgumentException("The upload carried no file.");
    }

    /// <summary><c>POST /containers/{container}/{verb}</c> → 204; failures surface as problem details.</summary>
    private static void MapLifecycle(RouteGroupBuilder group, string verb, Func<IContainerService, string, CancellationToken, Task> action)
    {
        group.MapPost($"/{{container}}/{verb}", async Task<NoContent> (string container, IContainerService containers, CancellationToken ct) =>
            {
                await action(containers, container, ct);
                return TypedResults.NoContent();
            })
            .WithName(char.ToUpperInvariant(verb[0]) + verb[1..] + "Container");
    }
}
