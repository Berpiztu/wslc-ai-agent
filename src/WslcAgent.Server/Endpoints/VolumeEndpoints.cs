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
        group.MapPost("/{name}/backup", (string name, BackupVolumeRequest request, VolumeBackups backups) => backups.StartBackup(name, request.Folder, request.Stop))
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
        api.MapBackups<VolumeBackups>("/volume-backups", "Volume");

        return api;
    }
}
