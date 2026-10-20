using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// What the image check said of each container it let be made with
/// vulnerabilities: one from a registry, which is never stopped, or any under
/// Warn. Said once when the container was made, it would be gone with the
/// line it was said on; kept here, in <c>image-warnings.json</c>, the list
/// carries it on the container itself (<see cref="ContainerSummary.ImageWarning"/>)
/// and its logs begin with it, for every client, across restarts of the
/// agent, until the container is gone.
/// </summary>
public sealed class ImageWarnings(IOptions<WslcOptions> options, ILogger<ImageWarnings> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path = Path.Combine(options.Value.DataDirectory, "image-warnings.json");
    private readonly Lock _gate = new();
    private ConcurrentDictionary<string, ImageWarning>? _warnings;

    /// <summary>
    /// Keeps what the check said for the container just made, by its id, with
    /// its name; nothing when it said nothing. <paramref name="refused"/>: its
    /// image is one the agent makes no container from (built here, Block).
    /// <paramref name="worst"/>: the worst severity it found.
    /// </summary>
    public void Remember(string id, string name, IReadOnlyList<string> said, bool refused = false, string worst = "")
    {
        if (said.Count == 0 || id.Trim().Length == 0)
        {
            return;
        }

        Warnings()[id.Trim()] = new ImageWarning(name.Trim(), DateTimeOffset.Now, [.. said], refused, worst);
        Save();
    }

    /// <summary>The container's image was checked again and is clean now: what was kept for it goes.</summary>
    public void Forget(string id)
    {
        var warnings = Warnings();
        foreach (var kept in warnings.Keys.Where(kept => Same(kept, id.Trim())).ToList())
        {
            warnings.TryRemove(kept, out _);
        }

        Save();
    }

    /// <summary>What was kept for a container, asked by its id or its name: what its logs begin with.</summary>
    public ImageWarning? Of(string container) =>
        Warnings().FirstOrDefault(kept => Same(kept.Key, container) || string.Equals(kept.Value.Name, container, StringComparison.Ordinal)).Value;

    /// <summary>
    /// The list's rows with what was kept for each, matched by id (a short id
    /// and a full one alike). On a list of every container, what was kept for
    /// one no longer there is let go.
    /// </summary>
    public IReadOnlyList<ContainerSummary> Annotate(IReadOnlyList<ContainerSummary> containers, bool complete)
    {
        var warnings = Warnings();
        if (warnings.IsEmpty)
        {
            return containers;
        }

        if (complete)
        {
            var gone = warnings.Keys.Where(id => !containers.Any(row => Same(id, row.Id))).ToList();
            if (gone.Count > 0)
            {
                foreach (var id in gone)
                {
                    warnings.TryRemove(id, out _);
                }

                Save();
            }
        }

        return [.. containers.Select(row => warnings.FirstOrDefault(kept => Same(kept.Key, row.Id)) is { Value: { } kept } ? row with { ImageWarning = kept.Text, ImageRefused = kept.Refused, ImageWorst = kept.Worst } : row)];
    }

    private static bool Same(string kept, string id) =>
        id.Length > 0 && (kept.StartsWith(id, StringComparison.OrdinalIgnoreCase) || id.StartsWith(kept, StringComparison.OrdinalIgnoreCase));

    private ConcurrentDictionary<string, ImageWarning> Warnings()
    {
        lock (_gate)
        {
            if (_warnings is null)
            {
                try
                {
                    _warnings = File.Exists(_path)
                        ? new ConcurrentDictionary<string, ImageWarning>(JsonSerializer.Deserialize<Dictionary<string, ImageWarning>>(File.ReadAllText(_path), Json) ?? [])
                        : new ConcurrentDictionary<string, ImageWarning>();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    logger.LogWarning("image warnings could not be read from {Path}: {Message}", _path, ex.Message);
                    _warnings = new ConcurrentDictionary<string, ImageWarning>();
                }
            }

            return _warnings;
        }
    }

    private void Save()
    {
        var warnings = Warnings();
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(warnings, Json));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("image warnings could not be written to {Path}: {Message}", _path, ex.Message);
            }
        }
    }
}

/// <summary>What the image check said of one container: its name when it was made, when, its findings, a line each, whether its image is one the agent refuses, and the worst severity it found (empty for a mark kept before it was).</summary>
public sealed record ImageWarning(string Name, DateTimeOffset At, IReadOnlyList<string> Lines, bool Refused = false, string Worst = "")
{
    /// <summary>The findings as one text, a line each: the list's <see cref="ContainerSummary.ImageWarning"/>.</summary>
    public string Text => string.Join("\n", Lines);
}
