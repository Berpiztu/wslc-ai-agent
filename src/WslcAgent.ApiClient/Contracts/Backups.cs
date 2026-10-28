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

/// <summary>Body of <c>POST /api/v1/containers/restore</c>: a container's backup put back, what it holds.</summary>
/// <param name="Folder">The backup, a folder of the agent's machine with its <c>backup.json</c>, by its full path.</param>
/// <param name="Container">The container it goes into, created when it is not there; empty for the one the backup was made of.</param>
public sealed record RestoreContainerRequest(string Folder, string Container = "");

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
public sealed record BackupSettings(
    string DefaultFolder,
    IReadOnlyList<BackupPlan>? Plans = null,
    string AllFolder = "",
    string DefaultStop = BackupChoice.Ask,
    string AllStop = BackupChoice.Default,
    string DefaultWhat = "",
    string AllWhat = BackupChoice.Default)
{
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
    /// kept apart (<c>…\Volume backups\hermes-data</c>). The agent's machine is
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
public sealed record BackupPlan(string Name, string Folder = "", string Stop = BackupChoice.Default, string What = BackupChoice.Default);

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
public sealed record BackupEntry(string Name, string Path, string What, long Bytes, DateTimeOffset CreatedAt, bool Exists);

/// <summary>Answer of <c>GET /api/v1/volume-backups</c> and <c>/container-backups</c>: the backups and restores under way or just ended, every backup of the history, the newest first, and where backups go.</summary>
public sealed record BackupList(IReadOnlyList<BackupJob> Jobs, IReadOnlyList<BackupEntry> History, BackupSettings? Settings = null);
