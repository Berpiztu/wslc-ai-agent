using Microsoft.AspNetCore.Http.HttpResults;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Volumes;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/volumes</c>: list, create, inspect, users, files, backup, restore, remove, prune. See docs/api-v1.md.</summary>
public static class VolumeEndpoints
{
    public static RouteGroupBuilder MapVolumeEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/volumes");

        group.MapGet("", (IVolumeService volumes, CancellationToken ct) => volumes.ListAsync(ct))
            .WithName("ListVolumes");

        group.MapPost("", async Task<NoContent> (CreateVolumeRequest request, IVolumeService volumes, CancellationToken ct) =>
            {
                await volumes.CreateAsync(request, ct);
                return TypedResults.NoContent();
            })
            .WithName("CreateVolume");

        group.MapPost("/prune", async Task<NoContent> (IVolumeService volumes, CancellationToken ct) =>
            {
                await volumes.PruneAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("PruneVolumes");

        group.MapGet("/{name}/inspect", (string name, IVolumeService volumes, CancellationToken ct) => volumes.InspectAsync(name, ct))
            .WithName("InspectVolume");

        group.MapGet("/{name}/containers", (string name, IVolumeService volumes, CancellationToken ct) => volumes.UsersAsync(name, ct))
            .WithName("VolumeUsers");

        // Files of a volume: a helper container with it mounted, browsed by the container files endpoints.
        group.MapPost("/{name}/files-session", (string name, FilesHelpers helpers, CancellationToken ct) => helpers.OpenVolumeAsync(name, ct))
            .WithName("OpenVolumeFiles");

        group.MapDelete("/{name}/files-session", async Task<NoContent> (string name, FilesHelpers helpers) =>
            {
                await helpers.CloseVolumeAsync(name);
                return TypedResults.NoContent();
            })
            .WithName("CloseVolumeFiles");

        // Backups and restores: started here, run by the agent, followed by the jobs below.
        group.MapPost("/{name}/backup", (string name, BackupVolumeRequest request, VolumeBackups backups) => backups.StartBackup(name, request.Folder))
            .WithName("BackupVolume");

        group.MapPost("/restore", (RestoreVolumeRequest request, VolumeBackups backups) => backups.StartRestore(request.File, request.Volume))
            .WithName("RestoreVolume");

        group.MapDelete("/{name}", async Task<NoContent> (string name, IVolumeService volumes, CancellationToken ct) =>
            {
                await volumes.RemoveAsync(name, ct);
                return TypedResults.NoContent();
            })
            .WithName("RemoveVolume");

        // Their own group: under /volumes a literal would hide a volume of that name.
        var jobs = api.MapGroup("/volume-backups");

        jobs.MapGet("", (VolumeBackups backups) => backups.List())
            .WithName("VolumeBackups");

        // Where backups go: the default folder and each volume's own. A job's id never reads "settings".
        jobs.MapGet("/settings", (VolumeBackups backups) => backups.Settings())
            .WithName("VolumeBackupSettings");

        jobs.MapPut("/settings", (VolumeBackupSettings settings, VolumeBackups backups) => backups.SaveSettings(settings))
            .WithName("SaveVolumeBackupSettings");

        // The backup and restore lines of the agent's log, of a volume or of every volume. Before /{id}: a literal.
        jobs.MapGet("/log", (string? volume, VolumeBackups backups) => backups.Log(volume))
            .WithName("VolumeBackupLog");

        // A backup of the history, by its file, which travels in the query as an image's reference does.
        jobs.MapDelete("/history", (string path, VolumeBackups backups) =>
            {
                backups.DeleteBackup(path);
                return TypedResults.NoContent();
            })
            .WithName("DeleteVolumeBackup");

        jobs.MapGet("/{id}", (string id, VolumeBackups backups) => backups.Get(id))
            .WithName("VolumeBackupJob");

        jobs.MapPost("/{id}/cancel", (string id, VolumeBackups backups) =>
            {
                backups.Cancel(id);
                return TypedResults.NoContent();
            })
            .WithName("CancelVolumeBackupJob");

        jobs.MapDelete("/{id}", (string id, VolumeBackups backups) =>
            {
                backups.Dismiss(id);
                return TypedResults.NoContent();
            })
            .WithName("DismissVolumeBackupJob");

        return api;
    }
}
