using System.Text.Json;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.AgentLog;
using WslcAgent.Server.Backups;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Projects;

/// <summary>
/// A project's backups and restores (<see cref="BackupService"/>), kept as the
/// tree they are shown as: in a folder named after the project, in its own
/// backup folder, else in the folder of every project (<see cref="FolderFor"/>),
/// a folder per backup by its date (<c>2026-10-28_065115</c>), holding in its root what the agent keeps
/// of the project (its Compose file and what it was read with,
/// <see cref="ProjectStore.CopyTo"/>) and <see cref="Manifest"/>; under it
/// <see cref="ContainersFolder"/>, each container's backup in a folder named
/// after it (<see cref="ContainerBackups.WriteAsync"/>, of the parts ticked,
/// kept in that container's history too), and <see cref="VolumesFolder"/>,
/// each volume's archive once however many of the containers use it. A
/// container of a project whose own settings name no folder is backed up the
/// same way, a date holding it alone (<see cref="ContainerBackups.StartBackupAsync"/>),
/// so a project's containers' backups are all in one place. Each container's
/// backup is a job of the container's own from the start, driven by the
/// project's (<see cref="BackupService.Track"/>), so its card's ring turns,
/// waiting its turn, then on its own steps, as a project's stop goes through
/// its containers; a container's cancelled stops the project's, and the
/// project's cancelled stops them all. Their volumes are backed up with every
/// container that uses them stopped, once for all of them
/// (<see cref="BackupStops"/>). A restore puts back the project's file and the
/// containers' backups chosen, each volume once; it is refused while one of
/// them runs.
/// </summary>
public sealed class ProjectBackups(IWslcRunner wslc, IContainerService containers, ContainerBackups containerBackups, BackupStops stops, ProjectStore projects,
    BackupBase backupBase, WslcEvents events, AgentLogs agentLogs, IOptions<WslcOptions> options, ILogger<ProjectBackups> logger)
    : BackupService("project", ChangeNotice.Container, wslc, backupBase, events, agentLogs, options.Value.DataDirectory, logger), IProjectBackups
{
    /// <summary>What a project's backup holds, in its folder's root.</summary>
    internal const string Manifest = "project-backup.json";

    /// <summary>Its containers' backups, a folder each, in its folder.</summary>
    internal const string ContainersFolder = "containers";

    /// <summary>Its volumes' archives, one each, in its folder.</summary>
    internal const string VolumesFolder = "volumes";

    /// <summary>Where a backup made before the agent kept a project's file in the root kept it.</summary>
    private const string FormerProjectFolder = "project";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>What a project's backups and restores write in the agent's log begins with, the project after it: the Logs page's Backups group.</summary>
    public const string Prefix = "project backup: ";

    public override string LogPrefix => Prefix;

    protected override string BaseSubfolder => "Projects";

    protected override string FormerBaseSubfolder => "Project backups";

    /// <summary>A project's backup is decided box by box in its tree (<see cref="BackupPlan.Picks"/>), not by parts for all of its containers.</summary>
    protected override bool InParts => false;

    public override async Task<BackupPreview> PreviewAsync(string name, string what, CancellationToken cancellationToken)
    {
        var project = WslcArgs.Require(name, "project");
        var (choices, _) = await ChoicesAsync(project, null, null, null, cancellationToken);
        return new BackupPreview(WhatOf(choices), await stops.RunningUsersAsync(VolumesOf(choices), cancellationToken), Settings().StopOf(project));
    }

    /// <summary>
    /// What a project's backup can hold, as its tree draws it: each of its
    /// containers with its image and its volumes, and what its own settings back
    /// up of it; and what the project's settings decide themselves.
    /// </summary>
    public async Task<ProjectTree> TreeAsync(string project, CancellationToken cancellationToken)
    {
        var name = WslcArgs.Require(project, "project");
        return TreeOf(name, await DetailsAsync(await MembersAsync(name, cancellationToken), cancellationToken), Settings().Plan(name).Picks);
    }

    private ProjectTree TreeOf(string project, IEnumerable<ContainerDetails> details, IReadOnlyDictionary<string, bool>? picks)
    {
        var own = containerBackups.Settings();
        return new ProjectTree(project, [.. details.Select(container =>
        {
            var what = own.WhatOf(container.Name);
            return new ProjectTreeContainer(container.Name, container.Image, ContainerBackups.VolumesOf(container),
                BackupWhat.Holds(what, BackupWhat.Config), BackupWhat.Holds(what, BackupWhat.Image),
                BackupWhat.Holds(what, BackupWhat.Volumes) ? containerBackups.BackedUpVolumesOf(container) : []);
        })], picks ?? new Dictionary<string, bool>());
    }

    /// <summary>The project's backup, as its settings decide it; <paramref name="stop"/>, the user agreed to stop what runs.</summary>
    public Task<BackupJob> StartBackupAsync(string project, string folder, bool stop, IReadOnlyDictionary<string, bool>? picks, CancellationToken cancellationToken) =>
        StartBackupAsync(project, folder, stop, picks, null, null, null, cancellationToken);

    /// <summary>
    /// The project's backup into a folder by its date: each box of its tree
    /// as <paramref name="picks"/> decide it (null: the project's settings),
    /// else as its container's own settings say; or, a container backed up
    /// alone (<paramref name="only"/>), that one with the parts it is given
    /// (<paramref name="onlyWhat"/>) and the stopping its settings say
    /// (<paramref name="stopSetting"/>), the Compose file with it.
    /// </summary>
    internal async Task<BackupJob> StartBackupAsync(string project, string folder, bool stop, IReadOnlyDictionary<string, bool>? picks,
        string? only, string? onlyWhat, string? stopSetting, CancellationToken cancellationToken)
    {
        var name = WslcArgs.Require(project, "project");
        var settings = Settings();
        var (choices, compose) = await ChoicesAsync(name, picks, only, onlyWhat, cancellationToken);
        if (choices.Count == 0 && !compose)
        {
            throw new ArgumentException($"Nothing of {name} is ticked to back up: neither its Compose file nor any part of a container.");
        }

        return Start(BackupJob.Backup, name, WhatOf(choices), Path.Combine(FolderFor(name, folder, settings), Stamp()),
            (job, ct) => BackupAsync(job, choices, compose, stopSetting ?? settings.StopOf(name), stop, ct));
    }

    /// <summary>
    /// What a backup of the project holds, container by container (one with
    /// nothing ticked is not in it), and whether its Compose file is:
    /// <paramref name="picks"/>, else the project's settings, deciding each box
    /// they name, its container's own settings every other; a container alone,
    /// the parts it is given, the volumes its settings keep.
    /// </summary>
    private async Task<(List<Choice> Choices, bool Compose)> ChoicesAsync(string project, IReadOnlyDictionary<string, bool>? picks, string? only, string? onlyWhat,
        CancellationToken cancellationToken)
    {
        var members = (await MembersAsync(project, cancellationToken)).Where(member => only is null || member == only).ToList();
        if (only is not null && members.Count == 0)
        {
            throw new ArgumentException($"{only} is not {project}'s.");
        }

        var details = await DetailsAsync(members, cancellationToken);
        if (only is not null)
        {
            var what = onlyWhat ?? "";
            return ([.. details.Select(container => new Choice(container, BackupWhat.Holds(what, BackupWhat.Config), BackupWhat.Holds(what, BackupWhat.Image),
                BackupWhat.Holds(what, BackupWhat.Volumes) ? containerBackups.BackedUpVolumesOf(container) : []))], true);
        }

        var decided = picks ?? Settings().Plan(project).Picks ?? new Dictionary<string, bool>();
        var tree = TreeOf(project, details, decided);
        var choices = details.Select(container => new Choice(container,
                tree.On(ProjectPicks.Configuration(container.Name), decided), tree.On(ProjectPicks.Image(container.Name), decided),
                [.. ContainerBackups.VolumesOf(container).Where(volume => tree.On(ProjectPicks.Volume(container.Name, volume), decided))]))
            .Where(choice => choice.Any)
            .ToList();
        return (choices, tree.On(ProjectPicks.Compose, decided));
    }

    /// <summary>What the backup holds of its containers, every part any of them gives; a project's Compose file alone is its configuration.</summary>
    private static string WhatOf(IEnumerable<Choice> choices) =>
        BackupWhat.Of(choices.SelectMany(choice => BackupWhat.PartsOf(choice.What))) is { Length: > 0 } what ? what : BackupWhat.Config;

    /// <summary>Every volume the backup holds, each once.</summary>
    private static List<string> VolumesOf(IEnumerable<Choice> choices) => [.. choices.SelectMany(choice => choice.Volumes).Distinct(StringComparer.Ordinal)];

    /// <summary>What a backup of the project holds of one of its containers.</summary>
    private sealed record Choice(ContainerDetails Container, bool Configuration, bool Image, IReadOnlyList<string> Volumes)
    {
        public bool Any => Configuration || Image || Volumes.Count > 0;

        public string What => BackupWhat.Of(new[] { (Configuration, BackupWhat.Config), (Image, BackupWhat.Image), (Volumes.Count > 0, BackupWhat.Volumes) }
            .Where(part => part.Item1).Select(part => part.Item2));
    }

    /// <summary>
    /// Where a project's backups go: always a folder named after it, in the
    /// folder asked for, else its own, else the folder of every project, so a
    /// folder shared by several projects keeps each one's dates apart
    /// (<c>…\projects\ia-apps\2026-10-28_065115</c>).
    /// </summary>
    private string FolderFor(string project, string? asked, BackupSettings settings)
    {
        var chosen = (asked ?? "").Trim().Length > 0 ? asked!.Trim()
            : settings.Plan(project).Folder is { Length: > 0 } own ? own
            : settings.FolderOfAll;
        return Path.Combine(FullPath(chosen, "backup folder"), project);
    }

    /// <summary>
    /// The project's backup in <paramref name="folder"/> put back: its Compose
    /// file when <paramref name="file"/> says so, and the containers of
    /// <paramref name="only"/>, every one when it is null, none when it is
    /// empty. Something has to be put back.
    /// </summary>
    public BackupJob StartRestore(string folder, string project, IReadOnlyList<string>? only, bool file)
    {
        var backup = WslcArgs.Require(folder, "backup folder");
        if (!Path.IsPathFullyQualified(backup) || ManifestAt(backup) is not { } held)
        {
            throw new ArgumentException($"{backup} is not a project's backup: its {Manifest} is not there, or cannot be read.");
        }

        var name = (project ?? "").Trim().Length > 0 ? project!.Trim() : held.Project;
        HashSet<string>? chosen = only is null ? null : [.. only.Where(container => container.Trim().Length > 0)];
        if (!file && chosen is { Count: 0 })
        {
            throw new ArgumentException("Nothing to restore: neither the Compose file nor any container is chosen.");
        }

        return Start(BackupJob.Restore, name, held.What, backup, (job, ct) => RestoreAsync(job, held, chosen, file, ct));
    }

    /// <summary>
    /// A project's backup listed with its containers' backups, as its
    /// <see cref="Manifest"/> names them, each with the volumes it holds, each
    /// said where it is and whether it still is: read from the backup, which
    /// says it whatever the history kept, a backup restored from included.
    /// </summary>
    protected override BackupEntry Shown(BackupEntry entry)
    {
        if (ManifestAt(entry.Path) is not { } held)
        {
            return entry with { Members = [] };
        }

        return entry with { Members = [.. held.Containers.Select(MemberOf)] };
    }

    /// <summary>A container's backup of a project's as it is listed: its folder's size, and what it holds, part by part (<see cref="ContainerBackups.ContentsOf"/>).</summary>
    private static BackupMember MemberOf(ProjectBackupMember member)
    {
        if (!Directory.Exists(member.Backup))
        {
            return new BackupMember(member.Container, member.Backup, 0, false);
        }

        try
        {
            return new BackupMember(member.Container, member.Backup, ContainerBackups.Size(member.Backup), true, ContainerBackups.ContentsOf(member.Backup));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new BackupMember(member.Container, member.Backup, 0, false);
        }
    }

    /// <summary>A project's backup deleted whole: its folder, with its containers' and its volumes', and each container's from its history.</summary>
    public override void DeleteBackup(string path)
    {
        var members = ManifestAt(path)?.Containers ?? [];
        base.DeleteBackup(path);
        foreach (var member in members)
        {
            DeleteContainers(member.Backup);
        }
    }

    /// <summary>
    /// One container's backup of a project's deleted, from the disk and its
    /// history, with the volumes' archives no other container of the backup
    /// uses, and the project's backup no longer holding it; the others stay as
    /// they are.
    /// </summary>
    public void DeleteMember(string path, string member)
    {
        var backup = WslcArgs.Require(path, "backup");
        var held = ManifestAt(backup) ?? throw new KeyNotFoundException($"{backup} is not a project's backup.");
        if (held.Containers.FirstOrDefault(each => string.Equals(each.Backup, member, StringComparison.OrdinalIgnoreCase)) is not { } gone)
        {
            throw new KeyNotFoundException($"{member} is not a container's backup {backup} holds.");
        }

        var left = held.Containers.Where(each => each != gone).ToList();
        var archives = ArchivesOf(gone).Except(left.SelectMany(ArchivesOf), StringComparer.OrdinalIgnoreCase).ToList();
        DeleteContainers(gone.Backup);
        archives.Where(File.Exists).ToList().ForEach(File.Delete);
        File.WriteAllText(Path.Combine(backup, Manifest), JsonSerializer.Serialize(held with { Containers = left }, Json));
        logger.LogInformation("{Container}'s backup {Backup} deleted from {Project}'s backup {Path}", gone.Container, gone.Backup, held.Project, backup);
        Notice(ChangeNotice.Container);
    }

    /// <summary>The volumes' archives a container's backup of a project's holds, by their full paths; none when it cannot be read.</summary>
    private static IEnumerable<string> ArchivesOf(ProjectBackupMember member)
    {
        try
        {
            var held = ContainerBackups.ManifestOf(member.Backup);
            return [.. held.Volumes.Select(volume => ContainerBackups.ArchiveOf(member.Backup, held, volume))];
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <summary>A container's backup gone, through its own history when it is in it, else from the disk alone.</summary>
    private void DeleteContainers(string backup)
    {
        try
        {
            containerBackups.DeleteBackup(backup);
        }
        catch (KeyNotFoundException)
        {
            if (Directory.Exists(backup))
            {
                Directory.Delete(backup, recursive: true);
            }
        }
    }

    /// <summary>What the project's backup at <paramref name="path"/> holds; null when it is not one, or cannot be read.</summary>
    private static ProjectBackupManifest? ManifestAt(string path)
    {
        try
        {
            var manifest = Path.Combine(path, Manifest);
            return File.Exists(manifest) ? JsonSerializer.Deserialize<ProjectBackupManifest>(File.ReadAllText(manifest), Json) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private async Task BackupAsync(BackupWork job, List<Choice> choices, bool compose, string stop, bool agreed, CancellationToken cancellationToken)
    {
        var volumes = VolumesOf(choices);
        var volumesFolder = Path.Combine(job.File, VolumesFolder);
        var tracked = new List<(Choice Choice, string Folder, BackupWork Job)>();
        job.Cancelling(() =>
        {
            tracked.ForEach(each => each.Job.Cancel());
            return Task.CompletedTask;
        });
        Directory.CreateDirectory(job.File);
        try
        {
            foreach (var choice in choices)
            {
                var folder = Path.Combine(job.File, ContainersFolder, choice.Container.Name);
                tracked.Add((choice, folder, containerBackups.Track(BackupJob.Backup, choice.Container.Name, choice.What, folder, $"Waiting for {job.Name}'s backup")));
            }

            if (compose && projects.CopyTo(job.Name, job.File))
            {
                job.Say($"INFO: {job.Name}'s Compose file, as the agent keeps it, is in the backup.");
            }

            // The networks of the containers whose configuration is in it, once for all of them, beside its volumes.
            await containerBackups.WriteNetworksAsync(job, choices.Where(choice => choice.Configuration).Select(choice => choice.Container),
                Path.Combine(job.File, ContainerBackups.NetworksFile), cancellationToken);

            var written = new List<ProjectBackupMember>();
            var packed = new HashSet<string>(StringComparer.Ordinal);
            async Task WriteAsync()
            {
                foreach (var (choice, folder, each) in tracked)
                {
                    var container = choice.Container;
                    job.Step($"Backing up {container.Name}");
                    try
                    {
                        await containerBackups.DriveAsync(each, cancellationToken,
                            token => containerBackups.WriteAsync(each, container, folder, choice.What, token, volumesFolder, packed, choice.Volumes));
                    }
                    // One of them cancelled from its own card stops the project's backup: what is left is not the project's.
                    catch (Exception) when (each.Token.IsCancellationRequested && !job.Token.IsCancellationRequested)
                    {
                        job.Cancel();
                        throw;
                    }

                    written.Add(new ProjectBackupMember(container.Name, folder));
                }
            }

            if (volumes.Count > 0)
            {
                await stops.StoppedAsync(job, job.Name, volumes, await stops.RunningUsersAsync(volumes, cancellationToken), stop, agreed, WriteAsync, cancellationToken);
            }
            else
            {
                await WriteAsync();
            }

            var held = new ProjectBackupManifest(job.Name, job.What, written, DateTimeOffset.Now);
            await File.WriteAllTextAsync(Path.Combine(job.File, Manifest), JsonSerializer.Serialize(held, Json), cancellationToken);
            var bytes = ContainerBackups.Size(job.File);
            Record(new BackupEntry(job.Name, job.File, job.What, bytes, held.CreatedAt, true));
            job.Say($"INFO: {job.File} written, {bytes:N0} bytes.");
        }
        catch
        {
            // Its containers' backups are in its folder: they go with it, and from their histories, once none of them
            // is still waiting its turn (a job under way keeps its folder).
            tracked.ForEach(each => containerBackups.Abandon(each.Job, $"Not backed up: {job.Name}'s backup ended before it."));
            tracked.ForEach(each => DeleteContainers(each.Folder));
            TryDelete(job.File);
            throw;
        }
        finally
        {
            tracked.ForEach(each => containerBackups.Abandon(each.Job, $"Not backed up: {job.Name}'s backup ended before it."));
        }
    }

    private async Task RestoreAsync(BackupWork job, ProjectBackupManifest held, HashSet<string>? only, bool file, CancellationToken cancellationToken)
    {
        var members = held.Containers.Where(member => only is null || only.Contains(member.Container)).ToList();
        var manifests = members.Select(member => (Member: member, Held: ContainerBackups.ManifestOf(member.Backup))).ToList();

        // Every one is checked before anything is put back: a project is not left half restored by one that runs.
        var running = (await containers.ListAsync(all: true, cancellationToken: cancellationToken)).Containers
            .Where(container => container.IsRunning && members.Any(member => member.Container == container.Name))
            .Select(container => container.Name)
            .ToList();
        if (running.Count > 0)
        {
            throw new InvalidOperationException($"{string.Join(", ", running)} {(running.Count == 1 ? "is" : "are")} running: stop {(running.Count == 1 ? "it" : "them")} first, then restore {job.Name}.");
        }

        if (file && (projects.RestoreFrom(job.Name, job.File) || projects.RestoreFrom(job.Name, Path.Combine(job.File, FormerProjectFolder))))
        {
            job.Say($"INFO: {job.Name}'s Compose file is back as the agent keeps it.");
        }

        // Each container's restore a job of its own, as each one's backup is: its ring turns, waiting, then on its steps.
        var tracked = manifests.Select(each => (each.Member, each.Held,
            Job: containerBackups.Track(BackupJob.Restore, each.Member.Container, each.Held.What, each.Member.Backup, $"Waiting for {job.Name}'s restore"))).ToList();
        job.Cancelling(() =>
        {
            tracked.ForEach(each => each.Job.Cancel());
            return Task.CompletedTask;
        });
        var unpacked = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var (member, manifest, each) in tracked)
            {
                job.Step($"Restoring {member.Container}");
                try
                {
                    await containerBackups.DriveAsync(each, cancellationToken,
                        token => containerBackups.RestoreFromAsync(each, member.Container, member.Backup, manifest, token, unpacked));
                }
                catch (Exception) when (each.Token.IsCancellationRequested && !job.Token.IsCancellationRequested)
                {
                    job.Cancel();
                    throw;
                }
            }
        }
        finally
        {
            tracked.ForEach(each => containerBackups.Abandon(each.Job, $"Not restored: {job.Name}'s restore ended before it."));
        }

        RecordRestored(job.Name, job.File, held.What, ContainerBackups.Size(job.File));
    }

    /// <summary>The project's containers, by name.</summary>
    private async Task<List<string>> MembersAsync(string project, CancellationToken cancellationToken) =>
        [.. (await containers.ListAsync(all: true, cancellationToken: cancellationToken)).Containers
            .Where(container => container.Project == project)
            .Select(container => container.Name)
            .Order(StringComparer.Ordinal)];

    private async Task<List<ContainerDetails>> DetailsAsync(IEnumerable<string> members, CancellationToken cancellationToken)
    {
        var details = new List<ContainerDetails>();
        foreach (var member in members)
        {
            details.Add(await containers.DetailsAsync(member, cancellationToken));
        }

        return details;
    }

    /// <summary>What a project's backup holds (<see cref="Manifest"/>).</summary>
    /// <param name="What">The parts of each container's backup.</param>
    /// <param name="Containers">Each container's backup, in its folder.</param>
    internal sealed record ProjectBackupManifest(string Project, string What, IReadOnlyList<ProjectBackupMember> Containers, DateTimeOffset CreatedAt);

    /// <summary>A container of a project's backup, and where its backup is.</summary>
    internal sealed record ProjectBackupMember(string Container, string Backup);
}
