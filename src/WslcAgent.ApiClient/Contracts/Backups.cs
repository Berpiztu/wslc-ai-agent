namespace WslcAgent.ApiClient.Contracts;

/// <summary>Body of <c>POST /api/v1/volumes/{name}/backup</c>: the host folder the backup is written in.</summary>
/// <param name="Folder">A folder of the agent's machine, by its full path, made when it is not there; empty for the volume's own backup folder (<see cref="BackupSettings"/>).</param>
/// <param name="Stop">The user agreed to stop the running containers that use it (<see cref="BackupPreview.Running"/>), started again when the backup ends; not needed when its settings say <see cref="BackupChoice.Yes"/>, and of no use when they say <see cref="BackupChoice.No"/>.</param>
public sealed record BackupVolumeRequest(string Folder = "", bool Stop = false);

/// <summary>Body of <c>POST /api/v1/volumes/restore</c>: a backup put back into a volume, which is created when it is not there.</summary>
/// <param name="File">The backup, a <c>.tar.gz</c> of the agent's machine, by its full path.</param>
/// <param name="Volume">The volume it goes into; its contents are replaced by the backup's.</param>
public sealed record RestoreVolumeRequest(string File, string Volume);

/// <summary>Body of <c>POST /api/v1/containers/{name}/backup</c>: what of the container, and the host folder it is written in.</summary>
/// <param name="What">Its parts, any of <see cref="BackupWhat.Config"/>, <see cref="BackupWhat.Image"/> and <see cref="BackupWhat.Volumes"/>, with commas between; empty for what its settings say (<see cref="BackupSettings.WhatOf"/>).</param>
/// <param name="Folder">A folder of the agent's machine, by its full path; empty for the container's own backup folder (<see cref="BackupSettings"/>).</param>
/// <param name="Stop">The user agreed to stop it and the running containers that use its volumes (<see cref="BackupPreview.Running"/>), started again when the backup ends; as a volume's (<see cref="BackupVolumeRequest.Stop"/>).</param>
public sealed record BackupContainerRequest(string What = "", string Folder = "", bool Stop = false);

/// <summary>Body of <c>POST /api/v1/projects/{name}/backup</c>: the project's backup, its file and each of its containers'.</summary>
/// <param name="Folder">A folder of the agent's machine, by its full path; empty for the project's own backup folder (<see cref="BackupSettings"/>).</param>
/// <param name="Stop">The user agreed to stop the running containers that use its containers' volumes (<see cref="BackupPreview.Running"/>), started again when the backup ends.</param>
/// <param name="Picks">What this backup decides itself, box by box (<see cref="ProjectPicks"/>), for it alone; null for what the project's settings decide. A box neither names follows its container's own settings.</param>
public sealed record BackupProjectRequest(string Folder = "", bool Stop = false, IReadOnlyDictionary<string, bool>? Picks = null);

/// <summary>
/// The boxes of a project's backup tree, by key: its Compose file, and each
/// container's configuration, image and each of its volumes. The project
/// decides a box it names (true ticked, false unticked); a box it does not
/// follows its container's own settings (<see cref="ProjectTreeContainer"/>).
/// A parent touched decides every box under it.
/// </summary>
public static class ProjectPicks
{
    public const string Compose = "compose";

    public static string Configuration(string container) => $"{container}/configuration";

    public static string Image(string container) => $"{container}/image";

    public static string Volume(string container, string volume) => $"{container}/volume/{volume}";

    /// <summary>Every box of one container: its configuration, its image, each of its volumes.</summary>
    public static IEnumerable<string> Of(ProjectTreeContainer container) =>
        [Configuration(container.Name), Image(container.Name), .. container.Volumes.Select(volume => Volume(container.Name, volume))];
}

/// <summary>
/// Answer of <c>GET /api/v1/project-backups/tree?name=</c>: what a project's
/// backup can hold, as its tree draws it: each of its containers with its
/// image and its volumes, and what its own settings back up of it; and what
/// the project's settings decide themselves (<see cref="Picks"/>).
/// </summary>
public sealed record ProjectTree(string Project, IReadOnlyList<ProjectTreeContainer> Containers, IReadOnlyDictionary<string, bool> Picks)
{
    /// <summary>A box as it stands: what <paramref name="picks"/> decide of it, else what its container's own settings say; the Compose file is backed up unless unticked.</summary>
    public bool On(string box, IReadOnlyDictionary<string, bool> picks)
    {
        if (picks.TryGetValue(box, out var picked))
        {
            return picked;
        }

        if (box == ProjectPicks.Compose)
        {
            return true;
        }

        var container = Containers.FirstOrDefault(each => box.StartsWith($"{each.Name}/", StringComparison.Ordinal));
        return container is not null && container.Own(box);
    }
}

/// <summary>A container of a project's backup tree, and what its own settings back up of it.</summary>
/// <param name="Image">The image it runs.</param>
/// <param name="Volumes">The volumes it has, every one.</param>
/// <param name="Configuration">Its own settings back up its configuration.</param>
/// <param name="ImageOn">Its own settings back up its image.</param>
/// <param name="VolumesOn">The volumes its own settings back up: none when they leave out its volumes, else every one but those they leave out.</param>
public sealed record ProjectTreeContainer(string Name, string Image, IReadOnlyList<string> Volumes, bool Configuration, bool ImageOn, IReadOnlyList<string> VolumesOn)
{
    /// <summary>One of its boxes as its own settings have it.</summary>
    public bool Own(string box) =>
        box == ProjectPicks.Configuration(Name) ? Configuration
        : box == ProjectPicks.Image(Name) ? ImageOn
        : VolumesOn.Any(volume => box == ProjectPicks.Volume(Name, volume));
}

/// <summary>Body of <c>POST /api/v1/projects/restore</c>: a project's backup put back, its file and its containers.</summary>
/// <param name="Folder">The backup, a folder of the agent's machine with its <c>project-backup.json</c>, by its full path.</param>
/// <param name="Project">The project it goes into; empty for the one the backup was made of.</param>
/// <param name="Containers">The containers of the backup put back, by name; null for every one, empty for none (its Compose file alone).</param>
/// <param name="File">Its Compose file put back as the agent keeps it.</param>
public sealed record RestoreProjectRequest(string Folder, string Project = "", IReadOnlyList<string>? Containers = null, bool File = true);

/// <summary>Body of <c>POST /api/v1/containers/restore</c>: a container's backup put back, what it holds.</summary>
/// <param name="Folder">The backup, a folder of the agent's machine with its <c>backup.json</c>, by its full path.</param>
/// <param name="Container">The container it goes into, created when it is not there; empty for the one the backup was made of.</param>
/// <param name="What">The parts of it put back, of those it holds (<see cref="BackupWhat"/>): its configuration alone, its image alone; empty for every one it holds.</param>
public sealed record RestoreContainerRequest(string Folder, string Container = "", string What = "");

/// <summary>
/// What a backup holds: a volume's, the volume; a container's, any of its
/// parts, <see cref="Config"/>, <see cref="Image"/>, <see cref="Volumes"/>,
/// written in that order with commas between (<c>config,volumes</c>).
/// </summary>
public static class BackupWhat
{
    /// <summary>A volume's contents, a <c>.tar.gz</c>.</summary>
    public const string Volume = "volume";

    /// <summary>A container's configuration: its settings, to make it again as it was.</summary>
    public const string Config = "config";

    /// <summary>A container's image.</summary>
    public const string Image = "image";

    /// <summary>A container's volumes' contents.</summary>
    public const string Volumes = "volumes";

    /// <summary>The parts a container's backup can hold, in the order they are offered and written.</summary>
    public static readonly IReadOnlyList<string> OfContainer = [Config, Image, Volumes];

    /// <summary>No part, while a person unticks them all to tick some again: never saved, nor backed up (what it is for waits for one).</summary>
    public const string Nothing = "none";

    /// <summary>A container whole, every part: what a backup holds until it is told otherwise.</summary>
    public static readonly string All = Of(OfContainer);

    /// <summary>
    /// The parts <paramref name="what"/> names, in their order; a backup made
    /// before a container's parts were chosen one by one says <c>all</c>, all
    /// of them. None for a volume's, or for what names none.
    /// </summary>
    public static IReadOnlyList<string> PartsOf(string? what)
    {
        var named = (what ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.ToLowerInvariant()).ToList();
        return named.Contains("all") ? OfContainer : [.. OfContainer.Where(named.Contains)];
    }

    /// <summary>The parts written as a backup's what: in their order, each once.</summary>
    public static string Of(IEnumerable<string> parts)
    {
        var chosen = parts.ToHashSet(StringComparer.Ordinal);
        return string.Join(',', OfContainer.Where(chosen.Contains));
    }

    /// <summary><paramref name="what"/> holds <paramref name="part"/>.</summary>
    public static bool Holds(string? what, string part) => PartsOf(what).Contains(part);

    /// <summary>How a person reads a part.</summary>
    public static string SaidPart(string part) => part switch
    {
        Config => "Configuration",
        Image => "Image",
        Volumes => "Volumes",
        _ => "Volume",
    };

    /// <summary>How a person reads what a backup holds: <c>All</c>, or its parts (<c>Configuration + Volumes</c>).</summary>
    public static string Said(string? what) => PartsOf(what) switch
    {
        { Count: 0 } => "Volume",
        { Count: 3 } => "All",
        var parts => string.Join(" + ", parts.Select(SaidPart)),
    };
}

/// <summary>
/// A backup setting's choices besides its values: <see cref="Default"/>, the
/// level below's. Whether a backup stops the running containers it needs
/// stopped is one of <see cref="Stops"/>; what a container's holds is the
/// parts ticked (<see cref="BackupWhat"/>).
/// </summary>
public static class BackupChoice
{
    /// <summary>The level below's: all of them's for one, the agent's default for all of them.</summary>
    public const string Default = "";

    /// <summary>Asked at each backup.</summary>
    public const string Ask = "ask";

    /// <summary>The running containers are stopped without asking, and started again when it ends.</summary>
    public const string Yes = "yes";

    /// <summary>They are not stopped: the backup is refused while they run.</summary>
    public const string No = "no";

    /// <summary>Whether a backup stops them, in the order offered.</summary>
    public static readonly IReadOnlyList<string> Stops = [Ask, Yes, No];

    /// <summary>How a person reads a choice.</summary>
    public static string Said(string choice) => choice switch
    {
        Default => "Default",
        Ask => "Ask",
        Yes => "Yes",
        _ => "No",
    };
}

/// <summary>
/// Where backups go, of volumes or of containers (<c>GET</c> and <c>PUT
/// /api/v1/volume-backups/settings</c>, <c>/container-backups/settings</c>),
/// and how they are made: three levels, each over the one below: one's own, in
/// its plan; else all of them's, set in the window of every one's backups; else
/// the agent's default, in Settings → Defaults. One with no folder of its own
/// backs up to a folder named after it in the folder of all of them. Whether a
/// backup stops the running containers it needs stopped (<see cref="BackupChoice.Stops"/>),
/// and, for a container, what it holds, go the same way.
/// </summary>
/// <param name="DefaultFolder">The agent's default (Settings → Defaults), a folder of its machine by its full path.</param>
/// <param name="Plans">Each one's plan, what it has of its own.</param>
/// <param name="AllFolder">The folder of all those with none of their own; empty for <paramref name="DefaultFolder"/>.</param>
/// <param name="DefaultStop">The agent's default on stopping them: <see cref="BackupChoice.Ask"/>, <see cref="BackupChoice.Yes"/> or <see cref="BackupChoice.No"/>.</param>
/// <param name="AllStop">All of them's; <see cref="BackupChoice.Default"/> for <paramref name="DefaultStop"/>.</param>
/// <param name="DefaultWhat">The agent's default of what a container's backup holds, its parts (<see cref="BackupWhat.Of"/>), all of them until others are ticked; a volume's is always the volume.</param>
/// <param name="AllWhat">All of them's; <see cref="BackupChoice.Default"/> for <paramref name="DefaultWhat"/>.</param>
/// <param name="BaseFolder">The backups' base folder (<see cref="BackupBaseFolder"/>), as the agent has it; read only, sent with the settings.</param>
/// <param name="BaseSubfolder">The folder of the base this kind's default is while it follows it: <c>Volumes</c>, <c>Containers</c>, <c>Projects</c>; read only.</param>
/// <param name="DefaultFromBase">The default folder follows the base: it is <see cref="BaseSubfolder"/> in it, and moves when the base does. Saved with an empty <paramref name="DefaultFolder"/>, or the base's own folder, it follows the base again.</param>
public sealed record BackupSettings(
    string DefaultFolder,
    IReadOnlyList<BackupPlan>? Plans = null,
    string AllFolder = "",
    string DefaultStop = BackupChoice.Ask,
    string AllStop = BackupChoice.Default,
    string DefaultWhat = "",
    string AllWhat = BackupChoice.Default,
    string BaseFolder = "",
    string BaseSubfolder = "",
    bool DefaultFromBase = false)
{
    /// <summary>
    /// The default folder <paramref name="baseFolder"/> gives this kind: its
    /// <see cref="BaseSubfolder"/> in it. The agent's machine is Windows: its
    /// separator, whatever client asks.
    /// </summary>
    public string InBase(string baseFolder) => $"{baseFolder.Trim().TrimEnd('\\', '/')}\\{BaseSubfolder}";

    /// <summary>The folder of all those with no folder of their own: the one set for all of them, else the default.</summary>
    public string FolderOfAll => AllFolder.Length > 0 ? AllFolder : DefaultFolder;

    /// <summary>Whether a backup of one with no choice of its own stops them: all of them's, else the default.</summary>
    public string StopOfAll => Over(AllStop, DefaultStop);

    /// <summary>Whether a backup of <paramref name="name"/> stops them: its own, else all of them's, else the default.</summary>
    public string StopOf(string name) => Over(PlanOf(name)?.Stop ?? "", StopOfAll);

    /// <summary>What a container's backup holds when it has no choice of its own: all of them's, else the default, else every part.</summary>
    public string WhatOfAll => Parts(AllWhat) ?? Parts(DefaultWhat) ?? BackupWhat.All;

    /// <summary>What a backup of <paramref name="name"/> holds, the parts ticked at its backup to begin with: its own, else all of them's, else the default.</summary>
    public string WhatOf(string name) => Parts(PlanOf(name)?.What) ?? WhatOfAll;

    /// <summary>One's plan, empty when it has none.</summary>
    public BackupPlan Plan(string name) => PlanOf(name) ?? new BackupPlan(name);

    /// <summary>The settings with <paramref name="plan"/> as its one's.</summary>
    public BackupSettings WithPlan(BackupPlan plan) =>
        this with { Plans = [.. (Plans ?? []).Where(kept => kept.Name != plan.Name), plan] };

    private BackupPlan? PlanOf(string name) => (Plans ?? []).FirstOrDefault(plan => plan.Name == name);

    /// <summary>A level's own choice, else the level below's; asked when neither says.</summary>
    private static string Over(string own, string below) => own.Length > 0 ? own : below.Length > 0 ? below : BackupChoice.Ask;

    /// <summary>A level's parts, written as a backup's what; null when it names none, for the level below.</summary>
    private static string? Parts(string? what) => BackupWhat.PartsOf(what) is { Count: > 0 } parts ? BackupWhat.Of(parts) : null;

    /// <summary>
    /// Where <paramref name="name"/>'s backups go: its own folder; else a folder
    /// named after it in the folder of all of them, so the backups of each are
    /// kept apart (<c>…\Volumes\hermes-data</c>). The agent's machine is
    /// Windows: its separator, whatever client asks.
    /// </summary>
    public string FolderOf(string name) =>
        PlanOf(name) is { Folder.Length: > 0 } plan
            ? plan.Folder
            : $"{FolderOfAll.TrimEnd('\\', '/')}\\{name}";
}

/// <summary>One's backup plan: what it has of its own; the place its schedule goes when backups are scheduled.</summary>
/// <param name="Name">The volume or the container.</param>
/// <param name="Folder">A folder of the agent's machine, by its full path; empty for the folder of all of them.</param>
/// <param name="Stop">Whether its backup stops the running containers it needs stopped (<see cref="BackupChoice.Stops"/>); <see cref="BackupChoice.Default"/> for all of them's.</param>
/// <param name="What">What a container's backup holds, its parts; <see cref="BackupChoice.Default"/> for all of them's.</param>
/// <param name="LeaveVolumes">A container's: the volumes its backup leaves out, unticked in its settings' tree; none, every volume it has is in it, one it is given later too.</param>
/// <param name="Picks">A project's: what its tree decides itself, box by box (<see cref="ProjectPicks"/>); every other box follows its container's own settings.</param>
public sealed record BackupPlan(string Name, string Folder = "", string Stop = BackupChoice.Default, string What = BackupChoice.Default, IReadOnlyList<string>? LeaveVolumes = null,
    IReadOnlyDictionary<string, bool>? Picks = null);

/// <summary>
/// Answer of <c>GET /api/v1/{kind}-backups/preview?name=&amp;what=</c>: what a
/// backup of one would do as its settings stand, for a client to ask what they
/// leave to the user before it starts.
/// </summary>
/// <param name="What">What it holds: the parts asked for, else its settings', the ones ticked when it is asked; a volume's is always the volume.</param>
/// <param name="Running">The running containers it has to stop, by name: a volume's, those that use it; a container's holding its volumes, those that use them, it among them; none for one without its volumes.</param>
/// <param name="Stop">Whether its settings stop them (<see cref="BackupChoice.Stops"/>).</param>
public sealed record BackupPreview(string What, IReadOnlyList<string> Running, string Stop);

/// <summary>
/// The folder every kind of backup's default starts from (<c>GET</c> and
/// <c>PUT /api/v1/backup-base</c>): a volume's, a container's and a project's
/// default is a folder of its own in it until it is given another.
/// </summary>
/// <param name="Folder">A folder of the agent's machine, by its full path.</param>
public sealed record BackupBaseFolder(string Folder);

/// <summary>
/// A backup or a restore the agent runs, of a volume or of a container:
/// started by a request that returns at once, and followed here until it ends.
/// A finished one stays listed a few seconds, a failed or cancelled one until
/// it is dismissed.
/// </summary>
/// <param name="Name">The volume or the container.</param>
/// <param name="Kind"><see cref="Backup"/> or <see cref="Restore"/>.</param>
/// <param name="What">What it holds (<see cref="BackupWhat"/>).</param>
/// <param name="State"><c>running</c>, <c>success</c>, <c>error</c> or <c>cancelled</c>.</param>
/// <param name="Status">The step under way, or how it ended.</param>
/// <param name="File">The backup written, or read: a volume's file, a container's folder.</param>
/// <param name="Output">What each step did and printed, a line each.</param>
public sealed record BackupJob(
    string Id,
    string Name,
    string Kind,
    string What,
    string State,
    string Status,
    string File,
    string Error,
    IReadOnlyList<string> Output,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt)
{
    public const string Backup = "backup";
    public const string Restore = "restore";

    public bool Running => State == "running";

    public bool Failed => State is "error" or "cancelled";
}

/// <summary>A backup the agent wrote, as its history keeps it.</summary>
/// <param name="Name">The volume or the container it is of; one restored from it lists it too.</param>
/// <param name="Path">A volume's <c>.tar.gz</c>, or a container's folder, on the agent's machine.</param>
/// <param name="What">What it holds (<see cref="BackupWhat"/>).</param>
/// <param name="Bytes">Its size when it was written.</param>
/// <param name="Exists">It is still where it was written: one moved or deleted cannot be restored from here.</param>
/// <param name="Members">A project's: each of its containers' backups, as its backup names them, under it as a project's containers are under it in a table; null for any other.</param>
/// <param name="Contents">A container's: what it holds, part by part; null for any other, or one that cannot be read.</param>
public sealed record BackupEntry(string Name, string Path, string What, long Bytes, DateTimeOffset CreatedAt, bool Exists, IReadOnlyList<BackupMember>? Members = null,
    BackupContents? Contents = null);

/// <summary>A container's backup a project's names, where it is, whether it still is, and what it holds.</summary>
/// <param name="Bytes">Its folder's: its settings and its image; its volumes are each their own.</param>
/// <param name="Contents">What it holds, part by part; null when it cannot be read.</param>
public sealed record BackupMember(string Name, string Path, long Bytes, bool Exists, BackupContents? Contents = null);

/// <summary>
/// What a container's backup holds, part by part, as its window's tree and a
/// project's show it under its date: its configuration, its image, its
/// volumes' archives.
/// </summary>
/// <param name="Configuration">It holds the container's settings, to make it again.</param>
/// <param name="Image">The image it holds, by the reference it ran; empty when it holds none.</param>
/// <param name="ImageBytes">Its image's archive's size; 0 when it is not there.</param>
/// <param name="ImageExists">Its image's archive is where it was written.</param>
/// <param name="Volumes">Its volumes' archives, in its folder, or in its project's backup's, kept once however many of its containers use one.</param>
/// <param name="Networks">With its configuration, the networks it is attached to, kept whole beside it (<c>networks.json</c>) and made again where they are missing when it is put back; none without.</param>
public sealed record BackupContents(bool Configuration, string Image, long ImageBytes, bool ImageExists, IReadOnlyList<BackupVolume> Volumes, IReadOnlyList<string>? Networks = null);

/// <summary>A volume's archive a backup holds, its <c>.tar.gz</c>, where it is, and whether it still is.</summary>
public sealed record BackupVolume(string Name, string Path, long Bytes, bool Exists);

/// <summary>Answer of <c>GET /api/v1/volume-backups</c> and <c>/container-backups</c>: the backups and restores under way or just ended, every backup of the history, the newest first, and where backups go.</summary>
public sealed record BackupList(IReadOnlyList<BackupJob> Jobs, IReadOnlyList<BackupEntry> History, BackupSettings? Settings = null);
