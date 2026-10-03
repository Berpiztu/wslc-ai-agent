using System.Text.Json;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Publishing;

/// <summary>
/// The published ports, kept in <c>publications.json</c> in the agent's data
/// folder: what the agent hands the proxy plugin, and what the container rows
/// and details are annotated with. Keyed by hostname, one port each.
/// </summary>
public sealed class PublicationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly ILogger<PublicationStore> _logger;
    private readonly Lock _gate = new();
    private readonly List<Publication> _publications;

    public PublicationStore(IOptions<WslcOptions> options, ILogger<PublicationStore> logger)
    {
        _logger = logger;
        _path = Path.Combine(options.Value.DataDirectory, "publications.json");
        _publications = Load();
    }

    public IReadOnlyList<Publication> All()
    {
        lock (_gate)
        {
            return _publications.OrderBy(p => p.Hostname, StringComparer.Ordinal).ToList();
        }
    }

    public Publication? Find(string hostname)
    {
        lock (_gate)
        {
            return _publications.FirstOrDefault(p => p.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>What a container's ports are published as, for its row and its details.</summary>
    public IReadOnlyList<Publication> ForContainer(string name)
    {
        lock (_gate)
        {
            return _publications.Where(p => p.Container.Equals(name, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.ContainerPort).ToList();
        }
    }

    public IReadOnlyList<ContainerSummary> Annotate(IReadOnlyList<ContainerSummary> containers) =>
        containers.Select(c => ForContainer(c.Name) is { Count: > 0 } published ? c with { Publications = published } : c).ToList();

    public void Add(Publication publication)
    {
        lock (_gate)
        {
            _publications.RemoveAll(p => p.Hostname.Equals(publication.Hostname, StringComparison.OrdinalIgnoreCase));
            _publications.Add(publication);
            Save();
        }
    }

    /// <summary>Takes the name out; false when it was not there.</summary>
    public bool Remove(string hostname)
    {
        lock (_gate)
        {
            var removed = _publications.RemoveAll(p => p.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                Save();
            }

            return removed;
        }
    }

    private List<Publication> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<List<Publication>>(File.ReadAllText(_path), JsonOptions) ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning("publications unreadable at {Path}: {Message}", _path, ex.Message);
        }

        return [];
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_publications, JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }
}
