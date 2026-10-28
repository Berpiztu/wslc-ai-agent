using Microsoft.AspNetCore.Http.HttpResults;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Backups;

namespace WslcAgent.Server.Endpoints;

/// <summary>
/// What every kind of backup answers, under its own path
/// (<c>/api/v1/volume-backups</c>, <c>/container-backups</c>): the jobs, the
/// history and where backups go; the settings; what a backup would do; the
/// log; a backup deleted; a job read, cancelled or dismissed. Starting a backup or a restore is the
/// resource's own (<c>/volumes/{name}/backup</c>, <c>/containers/restore</c>).
/// See docs/api-v1.md.
/// </summary>
public static class BackupEndpoints
{
    public static RouteGroupBuilder MapBackups<TService>(this RouteGroupBuilder api, string route, string label)
        where TService : BackupService
    {
        var jobs = api.MapGroup(route);

        jobs.MapGet("", (TService backups) => backups.List())
            .WithName($"{label}Backups");

        // Where backups go. The literals come before /{id}: a job's id never reads "settings".
        jobs.MapGet("/settings", (TService backups) => backups.Settings())
            .WithName($"{label}BackupSettings");

        jobs.MapPut("/settings", (BackupSettings settings, TService backups) => backups.SaveSettings(settings))
            .WithName($"Save{label}BackupSettings");

        // What a backup of one would do as its settings stand: what it holds, what it stops, whether to ask.
        jobs.MapGet("/preview", (string name, string? what, TService backups, CancellationToken cancellationToken) =>
                backups.PreviewAsync(name, what ?? "", cancellationToken))
            .WithName($"{label}BackupPreview");

        // The lines of one's backups and restores, or of all of them.
        jobs.MapGet("/log", (string? name, TService backups) => backups.Log(name))
            .WithName($"{label}BackupLog");

        // A backup of the history, by its file or folder, which travels in the query as an image's reference does.
        jobs.MapDelete("/history", (string path, TService backups) =>
            {
                backups.DeleteBackup(path);
                return TypedResults.NoContent();
            })
            .WithName($"Delete{label}Backup");

        jobs.MapGet("/{id}", (string id, TService backups) => backups.Get(id))
            .WithName($"{label}BackupJob");

        jobs.MapPost("/{id}/cancel", (string id, TService backups) =>
            {
                backups.Cancel(id);
                return TypedResults.NoContent();
            })
            .WithName($"Cancel{label}BackupJob");

        jobs.MapDelete("/{id}", (string id, TService backups) =>
            {
                backups.Dismiss(id);
                return TypedResults.NoContent();
            })
            .WithName($"Dismiss{label}BackupJob");

        return api;
    }
}
