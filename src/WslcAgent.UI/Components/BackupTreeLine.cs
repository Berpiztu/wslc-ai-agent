using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// A row of a backups tree (<c>BackupTree</c>), of a project or of a
/// container: a backup, by its date (<see cref="Date"/>), the parent; under
/// it what it holds, a step in for each level: its configuration (a project's
/// Compose file, a container's settings), and, a project's, each of its
/// containers' backups (<see cref="Container"/>) with their image and volumes
/// a step further; a container's, its image (<see cref="Image"/>) and its
/// volumes (<see cref="Volume"/>). Or what is under way, the backup or
/// restore and, a project's, each of its containers', the same way.
/// </summary>
/// <param name="Group">The backup, or its job, the row stands under: its own for a date's row.</param>
/// <param name="Kind"><see cref="Date"/>, <see cref="Configuration"/>, <see cref="Container"/>, <see cref="Image"/>, <see cref="Volume"/> or <see cref="Network"/>.</param>
/// <param name="Name">The project or the container; the image's reference on an image's row; the volume on a volume's.</param>
/// <param name="Depth">How many steps in from its date it stands.</param>
/// <param name="Backup">The backup the row is of, or stands under.</param>
/// <param name="Member">A project's container's backup, on its row and on its image's and volumes'.</param>
/// <param name="Job">The backup or restore under way, on a row of one.</param>
/// <param name="Archive">The volume's archive, on a volume's row.</param>
public sealed record BackupTreeLine(string Group, string Kind, string Name, int Depth, BackupEntry? Backup, BackupMember? Member = null, BackupJob? Job = null, BackupVolume? Archive = null)
{
    public const string Date = "date";
    public const string Configuration = "configuration";
    public const string Container = "container";
    public const string Image = "image";
    public const string Volume = "volume";

    /// <summary>A network its configuration is attached to, kept whole with it; shown, put back with the configuration.</summary>
    public const string Network = "network";

    /// <summary>The parent, a backup's date, or what is under it.</summary>
    public bool IsDate => Kind == Date;

    /// <summary>What the container's backup the row is of holds: a project's container's, or the container's own.</summary>
    public BackupContents? Contents => Member?.Contents ?? Backup?.Contents;

    /// <summary>The container's backup folder the row is of: a project's container's, or the container's own.</summary>
    public string Folder => Member?.Path ?? Backup?.Path ?? Job?.File ?? "";

    public DateTimeOffset When => Backup?.CreatedAt ?? Job?.StartedAt ?? default;

    public string What => Job?.What ?? Backup?.What ?? "";

    /// <summary>The size of what the row is: a volume's archive, an image's, a container's backup's folder, the whole backup on its date's; a configuration's is a few lines, and not said.</summary>
    public long Bytes => Kind switch
    {
        Configuration => 0,
        Volume => Archive?.Bytes ?? 0,
        Image => Contents?.ImageBytes ?? 0,
        Container => Member?.Bytes ?? 0,
        _ => Backup?.Bytes ?? 0,
    };

    /// <summary>Where it is: a volume's archive, the folder of the container's backup an image or a container is in, the backup's on its date's and its configuration's.</summary>
    public string Path => Kind switch
    {
        Volume => Archive?.Path ?? "",
        Image or Container => Folder,
        _ => Backup?.Path ?? Job?.File ?? "",
    };

    /// <summary>It is still where it was written; one under way is not a backup yet.</summary>
    public bool Exists => Kind switch
    {
        Volume => Archive?.Exists ?? false,
        Image => Contents?.ImageExists ?? false,
        Container => Member?.Exists ?? false,
        _ => Backup?.Exists ?? false,
    };
}
