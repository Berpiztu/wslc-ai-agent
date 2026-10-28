using System.Text.Json;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Backups;

/// <summary>
/// The folder every kind of backup's default folder starts from (Settings →
/// Defaults, at the top): a volume's, a container's and a project's default is
/// a folder of its own in it (<c>Volumes</c>, <c>Containers</c>,
/// <c>Projects</c>) until it is given another, so the base changed
/// moves every one that follows it at once (<see cref="BackupService.Settings"/>).
/// Kept in <c>backup-base.json</c>; the user's own <c>WSLC AI Agent</c> folder,
/// outside Documents, which OneDrive may carry away, until another is chosen.
/// </summary>
public sealed class BackupBase(WslcEvents events, IOptions<WslcOptions> options, ILogger<BackupBase> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path = Path.Combine(options.Value.DataDirectory, "backup-base.json");
    private readonly Lock _gate = new();

    /// <summary>The base until another is chosen.</summary>
    internal static readonly string First = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "WSLC AI Agent");

    public BackupBaseFolder Get()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(_path) && JsonSerializer.Deserialize<BackupBaseFolder>(File.ReadAllText(_path), Json) is { Folder.Length: > 0 } read)
                {
                    return read;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.LogWarning("The backups' base folder could not be read from {Path}: {Message}", _path, ex.Message);
            }

            return new BackupBaseFolder(First);
        }
    }

    /// <summary>The base saved, a full path of this machine; every kind's default that follows it moves with it.</summary>
    public BackupBaseFolder Save(BackupBaseFolder chosen)
    {
        var folder = WslcArgs.Require(chosen.Folder, "base folder").Trim();
        if (!Path.IsPathFullyQualified(folder))
        {
            throw new ArgumentException($"The base folder, {folder}, is not a full path of a folder of this machine.");
        }

        var saved = new BackupBaseFolder(folder);
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(saved, Json));
        }

        logger.LogInformation("The backups' base folder saved: {Folder}", folder);
        events.Publish(new ChangeNotice([ChangeNotice.Container, ChangeNotice.Volume]));
        return saved;
    }
}
