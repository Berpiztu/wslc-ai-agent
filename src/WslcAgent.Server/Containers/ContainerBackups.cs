using System.Text.Json;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.AgentLog;
using WslcAgent.Server.Backups;
using WslcAgent.Server.Projects;
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
public sealed class ContainerBackups(IWslcRunner wslc, IContainerService containers, INetworkService networks, VolumeBackups volumeBackups, BackupStops stops, BackupMarks marks,
    IServiceProvider services,
    BackupBase backupBase, WslcEvents events, AgentLogs agentLogs, IOptions<WslcOptions> options, ILogger<ContainerBackups> logger)
    : BackupService("container", ChangeNotice.Container, wslc, backupBase, events, agentLogs, options.Value.DataDirectory, logger), IContainerBackups
{
    /// <summary>What a container's backup holds, in its folder.</summary>
    internal const string Manifest = "backup.json";

    /// <summary>Its image, in its folder.</summary>
    internal const string ImageFile = "image.tar";

    /// <summary>Its volumes' archives, in its folder.</summary>
    internal const string VolumesFolder = "volumes";

    /// <summary>
    /// With its configuration, the networks it is attached to, each as it was
    /// made (driver, subnet, gateway, options): beside its manifest, or, a
    /// project's, once in the backup's own folder; the one beside its volumes'
    /// folder is the one (<see cref="NetworksFileOf"/>).
    /// </summary>
    internal const string NetworksFile = "networks.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>What a container's backups and restores write in the agent's log begins with, the container after it: the Logs page's Backups group.</summary>
    public const string Prefix = "container backup: ";

    public override string LogPrefix => Prefix;

    protected override string BaseSubfolder => "Containers";

    protected override string FormerBaseSubfolder => "Container backups";

    protected override bool InParts => true;

    protected override void Began(BackupWork job) => marks.Begin(job.Name, job.Kind);

    protected override void Ended(BackupWork job) => marks.End(job.Name);

    public override async Task<BackupPreview> PreviewAsync(string name, string what, CancellationToken cancellationToken)
    {
        var container = WslcArgs.Require(name, "container");
        var settings = Settings();
        var held = (what ?? "").Trim().Length > 0 ? Checked(what!) : settings.WhatOf(container);
        var running = BackupWhat.Holds(held, BackupWhat.Volumes)
            ? await stops.RunningUsersAsync(BackedUpVolumesOf(await containers.DetailsAsync(container, cancellationToken)), cancellationToken)
            : new List<string>();
        return new BackupPreview(held, running, settings.StopOf(container));
    }

    /// <summary>
    /// A container's backup asked for, where it belongs: one of a project's
    /// whose own settings name no folder (Default) goes into its project's
    /// backups, a date of them holding it alone, its volumes beside it
    /// (<see cref="ProjectBackups.StartBackup"/>), so a project's containers'
    /// backups are all in one place whether they are made from the project or
    /// one by one; any other as <see cref="StartBackup"/> does.
    /// </summary>
    public async Task<BackupJob> StartBackupAsync(string container, string what, string folder, bool stop, CancellationToken cancellationToken)
    {
        var name = WslcArgs.Require(container, "container");
        var settings = Settings();
        if ((folder ?? "").Trim().Length == 0 && settings.Plan(name).Folder.Length == 0
            && (await containers.ListAsync(all: true, cancellationToken: cancellationToken)).Containers.FirstOrDefault(row => row.Name == name) is { Project.Length: > 0 } member)
        {
            var held = (what ?? "").Trim().Length > 0 ? Checked(what!) : settings.WhatOf(name);
            // Resolved here, not given: a project's backups drive each of its containers' (they need these), and a
            // container of a project is backed up by them.
            return await services.GetRequiredService<ProjectBackups>().StartBackupAsync(member.Project, "", stop, null, name, held, settings.StopOf(name), cancellationToken);
        }

        return StartBackup(name, what ?? "", folder ?? "", stop);
    }

    /// <summary>
    /// The parts of <paramref name="container"/> asked for, else those its
    /// settings tick, into its own backup folder; <paramref name="stop"/>, the
    /// user agreed to stop what runs.
    /// </summary>
    public BackupJob StartBackup(string container, string what, string folder, bool stop)
    {
        var name = WslcArgs.Require(container, "container");
        var settings = Settings();
        var held = (what ?? "").Trim().Length > 0 ? Checked(what!) : settings.WhatOf(name);
        var directory = FullPath((folder ?? "").Trim().Length > 0 ? folder! : settings.FolderOf(name), "backup folder");
        var target = Path.Combine(directory, Stamp());
        return Start(BackupJob.Backup, name, held, target, (job, ct) => BackupAsync(job, settings.StopOf(name), stop, ct));
    }

    /// <summary>The parts asked for, written in their order; refused when it names none.</summary>
    private static string Checked(string what) =>
        BackupWhat.PartsOf(what) is { Count: > 0 } parts
            ? BackupWhat.Of(parts)
            : throw new ArgumentException($"{what} is not what a container's backup holds: any of {string.Join(", ", BackupWhat.OfContainer)}, with commas between.");

    internal static List<string> VolumesOf(ContainerDetails details) =>
        [.. details.Mounts.Where(mount => mount.Type == "volume").Select(mount => mount.Source).Distinct()];

    /// <summary>The volumes a backup of the container holds: every one it has but those its settings leave out (<see cref="BackupPlan.LeaveVolumes"/>).</summary>
    internal List<string> BackedUpVolumesOf(ContainerDetails details)
    {
        var left = Settings().Plan(details.Name).LeaveVolumes ?? [];
        return [.. VolumesOf(details).Where(volume => !left.Contains(volume))];
    }

    /// <summary>
    /// The backup in <paramref name="folder"/> put back into <paramref name="container"/>
    /// (empty: the one it was made of): the parts of <paramref name="what"/>
    /// it holds, its configuration alone or its image alone, else every one it
    /// holds.
    /// </summary>
    public BackupJob StartRestore(string folder, string container, string what = "")
    {
        var backup = WslcArgs.Require(folder, "backup folder");
        var manifest = Path.Combine(backup, Manifest);
        if (!Path.IsPathFullyQualified(backup) || !File.Exists(manifest))
        {
            throw new ArgumentException($"{backup} is not a container's backup: it has no {Manifest}.");
        }

        var held = ManifestOf(backup);
        if ((what ?? "").Trim().Length > 0)
        {
            var parts = BackupWhat.PartsOf(held.What).Intersect(BackupWhat.PartsOf(what)).ToList();
            held = parts.Count > 0
                ? held with { What = BackupWhat.Of(parts) }
                : throw new ArgumentException($"{backup} holds none of {what}: it holds {BackupWhat.Said(held.What)}.");
        }

        var name = (container ?? "").Trim().Length > 0 ? container!.Trim() : held.Container;
        return Start(BackupJob.Restore, name, held.What, backup, (job, ct) => RestoreFromAsync(job, name, backup, held, ct));
    }

    /// <summary>What a container's backup in <paramref name="backup"/> holds, read from its <see cref="Manifest"/>.</summary>
    internal static ContainerBackupManifest ManifestOf(string backup)
    {
        var manifest = Path.Combine(backup, Manifest);
        return File.Exists(manifest) && JsonSerializer.Deserialize<ContainerBackupManifest>(File.ReadAllText(manifest), Json) is { } held
            ? held
            : throw new ArgumentException($"{backup} is not a container's backup: its {Manifest} is not there, or cannot be read.");
    }

    private async Task BackupAsync(BackupWork job, string stop, bool agreed, CancellationToken cancellationToken)
    {
        job.Step($"Reading {job.Name}");
        var details = await containers.DetailsAsync(job.Name, cancellationToken);
        var volumes = BackedUpVolumesOf(details);

        // Its volumes are backed up with every container that uses them stopped, it among them, as a volume's are;
        // its configuration and its image are read while it runs.
        if (!BackupWhat.Holds(job.What, BackupWhat.Volumes))
        {
            await WriteAsync(job, details, job.File, job.What, cancellationToken);
            return;
        }

        var running = await stops.RunningUsersAsync(volumes, cancellationToken);
        await stops.StoppedAsync(job, job.Name, volumes, running, stop, agreed, () => WriteAsync(job, details, job.File, job.What, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// The backup of the container <paramref name="details"/> reads written in
    /// <paramref name="folder"/>, its parts <paramref name="what"/> and the
    /// manifest that says so, and kept in its history; what was written of it
    /// goes when it fails. Its own backup's, its volumes in its folder; and each
    /// of a project's (<see cref="ProjectBackups"/>), whose job says it, its
    /// volumes in the backup's <paramref name="volumesFolder"/>, each packed
    /// once however many of its containers use it (<paramref name="packed"/>).
    /// </summary>
    internal async Task<long> WriteAsync(BackupWork job, ContainerDetails details, string folder, string what, CancellationToken cancellationToken,
        string? volumesFolder = null, ISet<string>? packed = null, IReadOnlyList<string>? chosenVolumes = null)
    {
        // Its networks beside it, a container's own; a project's backup writes them once for all of its containers.
        if (volumesFolder is null && BackupWhat.Holds(what, BackupWhat.Config))
        {
            Directory.CreateDirectory(folder);
            await WriteNetworksAsync(job, [details], Path.Combine(folder, NetworksFile), cancellationToken);
        }

        // The volumes its project's tree chose, else those its own settings keep.
        var volumes = chosenVolumes?.ToList() ?? BackedUpVolumesOf(details);
        var archives = volumesFolder ?? Path.Combine(folder, VolumesFolder);
        foreach (var left in BackupWhat.Holds(what, BackupWhat.Volumes) ? VolumesOf(details).Except(volumes) : Enumerable.Empty<string>())
        {
            job.Say($"INFO: {left} is left out, as {(chosenVolumes is null ? $"{details.Name}'s backup settings say" : "its project's backup ticks it")}.");
        }

        foreach (var bind in details.Mounts.Where(mount => mount.Type != "volume"))
        {
            job.Say($"INFO: {bind.Source} is a folder of this machine mounted at {bind.Destination}, not a volume: it is not in the backup.");
        }

        Directory.CreateDirectory(folder);
        try
        {
            if (BackupWhat.Holds(what, BackupWhat.Image))
            {
                job.Step($"Saving image {details.Image}");
                await StepAsync(job, ["image", "save", "--output", Path.Combine(folder, ImageFile), details.Image], cancellationToken);
            }

            if (BackupWhat.Holds(what, BackupWhat.Volumes))
            {
                foreach (var volume in volumes)
                {
                    if (packed?.Add(volume) == false)
                    {
                        job.Say($"INFO: {volume} is in the backup already, packed with another container that uses it.");
                        continue;
                    }

                    await volumeBackups.PackAsync(job, volume, Path.Combine(archives, $"{volume}{VolumeBackups.Extension}"), cancellationToken);
                }
            }

            var held = new ContainerBackupManifest(details.Name, what, details.Image, volumes,
                BackupWhat.Holds(what, BackupWhat.Config) ? details.Form : null, DateTimeOffset.Now,
                volumesFolder is null ? null : Path.GetRelativePath(folder, volumesFolder));
            await File.WriteAllTextAsync(Path.Combine(folder, Manifest), JsonSerializer.Serialize(held, Json), cancellationToken);
            var bytes = Size(folder);
            Record(new BackupEntry(details.Name, folder, what, bytes, held.CreatedAt, true));
            job.Say($"INFO: {folder} written, {bytes:N0} bytes.");
            return bytes;
        }
        catch
        {
            TryDelete(folder);
            throw;
        }
    }

    /// <summary>
    /// What the backup in <paramref name="folder"/> holds put back into
    /// <paramref name="name"/>, and the backup kept among its own: its own
    /// restore's, and each of a project's, whose job says it.
    /// </summary>
    internal async Task RestoreFromAsync(BackupWork job, string name, string folder, ContainerBackupManifest held, CancellationToken cancellationToken,
        ISet<string>? unpacked = null)
    {
        var existing = (await containers.ListAsync(all: true, cancellationToken: cancellationToken)).Containers.FirstOrDefault(row => row.Name == name);
        // Its volumes are put back, and it is made again, with it stopped; a restore stops nothing.
        if (existing is { } running && (BackupWhat.Holds(held.What, BackupWhat.Volumes) || BackupWhat.Holds(held.What, BackupWhat.Config))
            && (await containers.DetailsAsync(running.Id, cancellationToken)).IsRunning)
        {
            throw new InvalidOperationException($"{name} is running: stop it first, then restore.");
        }

        if (BackupWhat.Holds(held.What, BackupWhat.Image))
        {
            job.Step($"Loading image {held.Image}");
            await StepAsync(job, ["image", "load", "--input", Path.Combine(folder, ImageFile)], cancellationToken);
        }

        if (BackupWhat.Holds(held.What, BackupWhat.Volumes))
        {
            foreach (var volume in held.Volumes)
            {
                // A volume two containers of a project's backup use is put back once.
                if (unpacked?.Add(volume) == false)
                {
                    continue;
                }

                await volumeBackups.UnpackAsync(job, volume, ArchiveOf(folder, held, volume), cancellationToken);
            }
        }

        if (BackupWhat.Holds(held.What, BackupWhat.Config) && held.Form is { } form)
        {
            await EnsureNetworksAsync(job, NetworksOf(form), NetworksFileOf(folder, held), cancellationToken);

            // Left stopped: it was stopped to be restored, and starting it is the user's to choose.
            var settings = form with { Name = name, Start = false };
            if (existing is { } container)
            {
                job.Step($"Making {name} again with its settings");
                await containers.RecreateAsync(container.Id, settings, cancellationToken);
            }
            else
            {
                job.Step($"Creating {name} with its settings");
                await containers.CreateAsync(settings, cancellationToken);
            }
        }

        RecordRestored(name, folder, held.What, Size(folder));
    }

    /// <summary>A container's backup as it is listed: with what it holds, part by part.</summary>
    protected override BackupEntry Shown(BackupEntry entry) => entry with { Contents = ContentsOf(entry.Path) };

    /// <summary>
    /// What the container's backup in <paramref name="folder"/> holds, part by
    /// part, each said where it is and whether it still is; null when it is not
    /// there, or cannot be read.
    /// </summary>
    internal static BackupContents? ContentsOf(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return null;
            }

            var held = ManifestOf(folder);
            var image = Path.Combine(folder, ImageFile);
            var holdsImage = BackupWhat.Holds(held.What, BackupWhat.Image);
            var volumes = BackupWhat.Holds(held.What, BackupWhat.Volumes)
                ? held.Volumes.Select(volume => ArchiveOf(folder, held, volume) is var archive && File.Exists(archive)
                    ? new BackupVolume(volume, archive, new FileInfo(archive).Length, true)
                    : new BackupVolume(volume, archive, 0, false)).ToList()
                : new List<BackupVolume>();
            var configuration = BackupWhat.Holds(held.What, BackupWhat.Config) && held.Form is not null;
            return new BackupContents(configuration,
                holdsImage ? held.Image : "", holdsImage && File.Exists(image) ? new FileInfo(image).Length : 0, holdsImage && File.Exists(image), volumes,
                configuration ? [.. NetworksOf(held.Form!)] : []);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The networks a container is attached to, by name: its own and those it is connected to; WSLC's own (bridge, host, none) are always there, and left out.</summary>
    internal static IEnumerable<string> NetworksOf(ContainerLaunchRequest form) =>
        new[] { form.Network }.Concat(form.ConnectNetworks).Where(network => network.Length > 0 && !NetworkSummary.IsBuiltIn(network)).Distinct(StringComparer.Ordinal);

    /// <summary>
    /// The networks <paramref name="details"/> are attached to, each as it was
    /// made, written in <paramref name="file"/>; none, no file. One that
    /// cannot be read is said in the log and left out.
    /// </summary>
    internal async Task WriteNetworksAsync(BackupWork job, IEnumerable<ContainerDetails> details, string file, CancellationToken cancellationToken)
    {
        var made = new List<CreateNetworkRequest>();
        foreach (var network in details.SelectMany(container => NetworksOf(container.Form)).Distinct(StringComparer.Ordinal))
        {
            try
            {
                made.Add((await networks.DetailsAsync(network, cancellationToken)).Form);
            }
            catch (Exception ex) when (ex is WslcException or WslcNotFoundException or KeyNotFoundException)
            {
                job.Say($"WARNING: network {network} could not be read, and is not in the backup: {ex.Message}");
            }
        }

        if (made.Count > 0)
        {
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(made, Json), cancellationToken);
            job.Say($"INFO: its network{(made.Count == 1 ? "" : "s")} {string.Join(", ", made.Select(network => network.Name))} {(made.Count == 1 ? "is" : "are")} in the backup, as made.");
        }
    }

    /// <summary>
    /// Each of <paramref name="needed"/> made again from <paramref name="file"/>
    /// where it is missing, before the container that is attached to it; one
    /// that is there is left as it is, and said when its subnet is not the
    /// backup's.
    /// </summary>
    private async Task EnsureNetworksAsync(BackupWork job, IEnumerable<string> needed, string file, CancellationToken cancellationToken)
    {
        var wanted = needed.ToList();
        if (wanted.Count == 0 || !File.Exists(file))
        {
            return;
        }

        var kept = JsonSerializer.Deserialize<List<CreateNetworkRequest>>(await File.ReadAllTextAsync(file, cancellationToken), Json) ?? [];
        var there = (await networks.ListAsync(cancellationToken)).Networks.ToDictionary(network => network.Name, StringComparer.Ordinal);
        foreach (var network in kept.Where(network => wanted.Contains(network.Name)))
        {
            if (there.TryGetValue(network.Name, out var existing))
            {
                if (network.Subnet.Length > 0 && existing.Subnet != network.Subnet)
                {
                    job.Say($"WARNING: network {network.Name} is here already with subnet {existing.Subnet}, not the backup's {network.Subnet}: it is left as it is.");
                }

                continue;
            }

            job.Step($"Creating network {network.Name}");
            await networks.CreateAsync(network, cancellationToken);
            there[network.Name] = new NetworkSummary("", network.Name, network.Driver, "", network.Subnet, network.Gateway, network.Internal, false, 0, 0, 0, 0);
        }
    }

    /// <summary>Where a container's backup keeps its networks: beside its volumes' folder, its own or its project's backup's.</summary>
    internal static string NetworksFileOf(string folder, ContainerBackupManifest held) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Path.Combine(folder, held.VolumesIn ?? VolumesFolder)))!, NetworksFile);

    /// <summary>Where a container's backup keeps <paramref name="volume"/>'s archive: in its own folder, or in its project's backup's, where the manifest says.</summary>
    internal static string ArchiveOf(string folder, ContainerBackupManifest held, string volume) =>
        Path.GetFullPath(Path.Combine(folder, held.VolumesIn ?? VolumesFolder, $"{volume}{VolumeBackups.Extension}"));

    /// <summary>What a folder holds, every file in it.</summary>
    internal static long Size(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);

    /// <summary>What a container's backup holds (<see cref="Manifest"/>).</summary>
    /// <param name="Container">The container it was made of.</param>
    /// <param name="Image">Its image, by the reference it ran.</param>
    /// <param name="Volumes">Its volumes, by name; each one's archive is in <see cref="VolumesFolder"/>.</param>
    /// <param name="What">Its parts (<see cref="BackupWhat"/>); <c>all</c>, <c>image</c> or <c>volumes</c> in one made before they were ticked one by one.</param>
    /// <param name="Form">Its settings, to make it again as it was; only in a backup that holds its configuration.</param>
    /// <param name="VolumesIn">Where its volumes' archives are, from its folder: its project's backup's <c>volumes</c> (<c>..\..\volumes</c>), kept once for all of its containers; null for its own <see cref="VolumesFolder"/>.</param>
    internal sealed record ContainerBackupManifest(string Container, string What, string Image, IReadOnlyList<string> Volumes, ContainerLaunchRequest? Form, DateTimeOffset CreatedAt,
        string? VolumesIn = null);
}
