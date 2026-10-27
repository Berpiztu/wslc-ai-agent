using System.Text.Json;
using Microsoft.Extensions.Options;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// The services of projects whose container Zaindari's patch moved to it:
/// which project and service, the image its file names and the id that image
/// had when it was patched, the patch, and when. A project's save reads its
/// file again, which names the original; by this record it brings the service
/// up on the patch instead, and says so, for as long as the file names that
/// image and the image is the one patched. A file that names another, a newer
/// original (pulled anew, built again) or a patch gone from the machine lets
/// the record go, and the service runs what its file says. Kept in
/// <c>patched-services.json</c>, across restarts of the agent.
/// </summary>
public sealed class PatchedServices(IOptions<WslcOptions> options, ILogger<PatchedServices> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path = Path.Combine(options.Value.DataDirectory, "patched-services.json");
    private readonly Lock _gate = new();
    private List<PatchedService>? _records;

    /// <summary>The project's services on a patch.</summary>
    public IReadOnlyList<PatchedService> Of(string project)
    {
        lock (_gate)
        {
            return [.. Records().Where(record => record.Project == project)];
        }
    }

    /// <summary>The record of one service; null when it runs what its file says.</summary>
    public PatchedService? Of(string project, string service)
    {
        lock (_gate)
        {
            return Records().FirstOrDefault(record => record.Project == project && record.Service == service);
        }
    }

    /// <summary>The service moved to a patch, over any record it had.</summary>
    public void Record(PatchedService patched)
    {
        lock (_gate)
        {
            Records().RemoveAll(record => record.Project == patched.Project && record.Service == patched.Service);
            Records().Add(patched);
            Save();
        }
    }

    /// <summary>The service runs what its file says again.</summary>
    public void Forget(string project, string service)
    {
        lock (_gate)
        {
            if (Records().RemoveAll(record => record.Project == project && record.Service == service) > 0)
            {
                Save();
            }
        }
    }

    private List<PatchedService> Records()
    {
        if (_records is null)
        {
            try
            {
                _records = File.Exists(_path) ? JsonSerializer.Deserialize<List<PatchedService>>(File.ReadAllText(_path), Json) ?? [] : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.LogWarning("patched services could not be read from {Path}: {Message}", _path, ex.Message);
                _records = [];
            }
        }

        return _records;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Records(), Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("patched services could not be written to {Path}: {Message}", _path, ex.Message);
        }
    }
}

/// <summary>A project's service on Zaindari's patch: the image its file names, that image's id when it was patched, the patch, and when.</summary>
public sealed record PatchedService(string Project, string Service, string Original, string OriginalId, string Patch, DateTimeOffset At);
