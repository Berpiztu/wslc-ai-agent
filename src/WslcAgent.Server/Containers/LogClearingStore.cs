using System.Text.Json;
using Microsoft.Extensions.Options;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// When each container's logs were last cleared in a viewer: the stamp of the last line
/// cleared, on the container's own clock. wslc keeps a container's logs until the container
/// is removed and has no command to empty them, so a clear can only hide what came before;
/// kept here, it holds when the viewer is opened again and on every device. Keyed by the
/// name or id the viewer asks with; a container recreated under the same name writes only
/// later lines, which the old stamp lets through.
/// </summary>
public sealed class LogClearingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly ILogger<LogClearingStore> _logger;
    private readonly Lock _gate = new();
    private Dictionary<string, DateTimeOffset> _cleared;

    public LogClearingStore(IOptions<WslcOptions> options, ILogger<LogClearingStore> logger)
    {
        _logger = logger;
        _path = Path.Combine(options.Value.DataDirectory, "log-clearings.json");
        _cleared = Load();
    }

    public DateTimeOffset? Get(string container)
    {
        lock (_gate)
        {
            return _cleared.TryGetValue(Key(container), out var at) ? at : null;
        }
    }

    public void Set(string container, DateTimeOffset at)
    {
        lock (_gate)
        {
            _cleared[Key(container)] = at;
            Save();
        }
    }

    /// <summary>The history whole again.</summary>
    public void Remove(string container)
    {
        lock (_gate)
        {
            if (_cleared.Remove(Key(container)))
            {
                Save();
            }
        }
    }

    private static string Key(string container) => container.Trim().TrimStart('/').ToLowerInvariant();

    private Dictionary<string, DateTimeOffset> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(File.ReadAllText(_path)) ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning("log clearings unreadable at {Path}: {Message}", _path, ex.Message);
        }

        return [];
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_cleared, JsonOptions));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("log clearings not saved at {Path}: {Message}", _path, ex.Message);
        }
    }
}
