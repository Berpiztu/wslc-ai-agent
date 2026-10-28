using System.Text.Json;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.AgentLog;
using WslcAgent.Server.Backups;
using WslcAgent.Server.Volumes;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// A container's backups and restores (<see cref="BackupService"/>), of the
/// parts ticked (<see cref="BackupWhat"/>): its configuration (its settings,
/// to make it again as it was), its image, its volumes, any of them. A backup
/// is a folder, <c>&lt;container&gt;-&lt;date&gt;</c>: <see cref="Manifest"/>
/// says what it holds, <see cref="ImageFile"/> the image (<c>wslc image save</c>),
/// <see cref="VolumesFolder"/> a <c>.tar.gz</c> per volume, packed as a
/// volume's own backup is (<see cref="VolumeBackups.PackAsync"/>). A host folder
/// mounted into it is the machine's, not the container's: it is not in the
/// backup, and the log says so. A restore puts back what the backup holds: the
/// image loaded, each volume's contents replaced, created when it is not there,
/// and, with its configuration, the container made again, stopped, with its
/// settings, under the name asked for: the one it had, or a new machine's. Its
/// volumes are backed up with every container that uses them stopped, it
/// among them, as its settings say (<see cref="BackupStops"/>), and they are
/// started again; its configuration and its image are read while it runs. A
/// restore is refused while it, or one that uses its volumes, runs.
/// </summary>
public sealed class ContainerBackups(IWslcRunner wslc, IContainerService containers, VolumeBackups volumeBackups, BackupStops stops, WslcEvents events, AgentLogs agentLogs, IOptions<WslcOptions> options, ILogger<ContainerBackups> logger)
    : BackupService("container", ChangeNotice.Container, wslc, events, agentLogs, options.Value.DataDirectory, logger), IContainerBackups
{
    /// <summary>What a container's backup holds, in its folder.</summary>
    internal const string Manifest = "backup.json";

    /// <summary>Its image, in its folder.</summary>
    internal const string ImageFile = "image.tar";

    /// <summary>Its volumes' archives, in its folder.</summary>
    internal const string VolumesFolder = "volumes";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>What a container's backups and restores write in the agent's log begins with, the container after it: the Logs page's Backups group.</summary>
    public const string Prefix = "container backup: ";

    public override string LogPrefix => Prefix;

    protected override string FirstDefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "WSLC AI Agent", "Container backups");

    protected override bool InParts => true;

    public override async Task<BackupPreview> PreviewAsync(string name, string what, CancellationToken cancellationToken)
    {
        var container = WslcArgs.Require(name, "container");
        var settings = Settings();
        var held = (what ?? "").Trim().Length > 0 ? Checked(what!) : settings.WhatOf(container);
        var running = BackupWhat.Holds(held, BackupWhat.Volumes)
            ? await stops.RunningUsersAsync(VolumesOf(await containers.DetailsAsync(container, cancellationToken)), cancellationToken)
            : new List<string>();
        return new BackupPreview(held, running, settings.StopOf(container));
    }

    /// <summary>
    /// The parts of <paramref name="container"/> asked for, else those its
    /// settings tick; <paramref name="stop"/>, the user agreed to stop what runs.
    /// </summary>
    public BackupJob StartBackup(string container, string what, string folder, bool stop)
    {
        var name = WslcArgs.Require(container, "container");
        var settings = Settings();
        var held = (what ?? "").Trim().Length > 0 ? Checked(what!) : settings.WhatOf(name);
        var directory = FullPath((folder ?? "").Trim().Length > 0 ? folder! : settings.FolderOf(name), "backup folder");
        var target = Path.Combine(directory, $"{name}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
        return Start(BackupJob.Backup, name, held, target, (job, ct) => BackupAsync(job, settings.StopOf(name), stop, ct));
    }

    /// <summary>The parts asked for, written in their order; refused when it names none.</summary>
    private static string Checked(string what) =>
        BackupWhat.PartsOf(what) is { Count: > 0 } parts
            ? BackupWhat.Of(parts)
            : throw new ArgumentException($"{what} is not what a container's backup holds: any of {string.Join(", ", BackupWhat.OfContainer)}, with commas between.");

    private static List<string> VolumesOf(ContainerDetails details) =>
        [.. details.Mounts.Where(mount => mount.Type == "volume").Select(mount => mount.Source).Distinct()];

    public BackupJob StartRestore(string folder, string container)
    {
        var backup = WslcArgs.Require(folder, "backup folder");
        var manifest = Path.Combine(backup, Manifest);
        if (!Path.IsPathFullyQualified(backup) || !File.Exists(manifest))
        {
            throw new ArgumentException($"{backup} is not a container's backup: it has no {Manifest}.");
        }

        var held = JsonSerializer.Deserialize<ContainerBackupManifest>(File.ReadAllText(manifest), Json)
            ?? throw new ArgumentException($"{manifest} cannot be read.");
        var name = (container ?? "").Trim().Length > 0 ? container!.Trim() : held.Container;
        return Start(BackupJob.Restore, name, held.What, backup, (job, ct) => RestoreAsync(job, held, ct));
    }

    private async Task BackupAsync(BackupWork job, string stop, bool agreed, CancellationToken cancellationToken)
    {
        job.Step($"Reading {job.Name}");
        var details = await containers.DetailsAsync(job.Name, cancellationToken);
        var volumes = VolumesOf(details);
        foreach (var bind in details.Mounts.Where(mount => mount.Type != "volume"))
        {
            job.Say($"INFO: {bind.Source} is a folder of this machine mounted at {bind.Destination}, not a volume: it is not in the backup.");
        }

        // Its volumes are backed up with every container that uses them stopped, it among them, as a volume's are;
        // its configuration and its image are read while it runs.
        if (!BackupWhat.Holds(job.What, BackupWhat.Volumes))
        {
            await WriteAsync(job, details, volumes, cancellationToken);
            return;
        }

        var running = await stops.RunningUsersAsync(volumes, cancellationToken);
        await stops.StoppedAsync(job, job.Name, volumes, running, stop, agreed, () => WriteAsync(job, details, volumes, cancellationToken), cancellationToken);
    }

    /// <summary>The backup's folder written: what it holds, and the manifest that says so; what was written of it goes when it fails.</summary>
    private async Task WriteAsync(BackupWork job, ContainerDetails details, List<string> volumes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(job.File);
        try
        {
            if (BackupWhat.Holds(job.What, BackupWhat.Image))
            {
                job.Step($"Saving image {details.Image}");
                await StepAsync(job, ["image", "save", "--output", Path.Combine(job.File, ImageFile), details.Image], cancellationToken);
            }

            if (BackupWhat.Holds(job.What, BackupWhat.Volumes))
            {
                foreach (var volume in volumes)
                {
                    await volumeBackups.PackAsync(job, volume, Path.Combine(job.File, VolumesFolder, $"{volume}{VolumeBackups.Extension}"), cancellationToken);
                }
            }

            var held = new ContainerBackupManifest(details.Name, job.What, details.Image, volumes,
                BackupWhat.Holds(job.What, BackupWhat.Config) ? details.Form : null, DateTimeOffset.Now);
            await File.WriteAllTextAsync(Path.Combine(job.File, Manifest), JsonSerializer.Serialize(held, Json), cancellationToken);
            var bytes = Size(job.File);
            Record(new BackupEntry(job.Name, job.File, job.What, bytes, held.CreatedAt, true));
            job.Say($"INFO: {job.File} written, {bytes:N0} bytes.");
        }
        catch
        {
            TryDelete(job.File);
            throw;
        }
    }

    private async Task RestoreAsync(BackupWork job, ContainerBackupManifest held, CancellationToken cancellationToken)
    {
        var existing = (await containers.ListAsync(all: true, cancellationToken: cancellationToken)).Containers.FirstOrDefault(row => row.Name == job.Name);
        // Its volumes are put back, and it is made again, with it stopped; a restore stops nothing.
        if (existing is { } running && (BackupWhat.Holds(job.What, BackupWhat.Volumes) || BackupWhat.Holds(job.What, BackupWhat.Config))
            && (await containers.DetailsAsync(running.Id, cancellationToken)).IsRunning)
        {
            throw new InvalidOperationException($"{job.Name} is running: stop it first, then restore.");
        }

        if (BackupWhat.Holds(job.What, BackupWhat.Image))
        {
            job.Step($"Loading image {held.Image}");
            await StepAsync(job, ["image", "load", "--input", Path.Combine(job.File, ImageFile)], cancellationToken);
        }

        if (BackupWhat.Holds(job.What, BackupWhat.Volumes))
        {
            foreach (var volume in held.Volumes)
            {
                await volumeBackups.UnpackAsync(job, volume, Path.Combine(job.File, VolumesFolder, $"{volume}{VolumeBackups.Extension}"), cancellationToken);
            }
        }

        if (BackupWhat.Holds(job.What, BackupWhat.Config) && held.Form is { } form)
        {
            // Left stopped: it was stopped to be restored, and starting it is the user's to choose.
            var settings = form with { Name = job.Name, Start = false };
            if (existing is { } container)
            {
                job.Step($"Making {job.Name} again with its settings");
                await containers.RecreateAsync(container.Id, settings, cancellationToken);
            }
            else
            {
                job.Step($"Creating {job.Name} with its settings");
                await containers.CreateAsync(settings, cancellationToken);
            }
        }

        RecordRestored(job.Name, job.File, job.What, Size(job.File));
    }

    /// <summary>What a folder holds, every file in it.</summary>
    private static long Size(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);

    /// <summary>What a container's backup holds (<see cref="Manifest"/>).</summary>
    /// <param name="Container">The container it was made of.</param>
    /// <param name="Image">Its image, by the reference it ran.</param>
    /// <param name="Volumes">Its volumes, by name; each one's archive is in <see cref="VolumesFolder"/>.</param>
    /// <param name="What">Its parts (<see cref="BackupWhat"/>); <c>all</c>, <c>image</c> or <c>volumes</c> in one made before they were ticked one by one.</param>
    /// <param name="Form">Its settings, to make it again as it was; only in a backup that holds its configuration.</param>
    internal sealed record ContainerBackupManifest(string Container, string What, string Image, IReadOnlyList<string> Volumes, ContainerLaunchRequest? Form, DateTimeOffset CreatedAt);
}
