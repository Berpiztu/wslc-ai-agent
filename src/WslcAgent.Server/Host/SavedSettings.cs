using System.Text.Json;

namespace WslcAgent.Server.Host;

/// <summary>
/// One of Settings' records, kept as a JSON file in the agent's data folder and
/// served from memory: what the operator saves is what the running agent obeys
/// on its next call, with no restart, and it is still there after one. The
/// file is read again whenever it changed since it was last read: another
/// agent on the same data folder, or a hand, may write it (auto-update
/// switched off from a development agent, while a production agent that had
/// read it hours before updated itself).
/// </summary>
public abstract class SavedSettings<T> where T : class
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly Lock _gate = new();
    private readonly T _defaults;
    private T _settings;

    /// <summary>When the file was last written, as it was when it was read or written here; null while there is none.</summary>
    private DateTime? _written;

    /// <param name="path">The file, in the agent's data folder.</param>
    /// <param name="defaults">What stands until the file exists, or when it cannot be read.</param>
    protected SavedSettings(string path, T defaults)
    {
        _path = path;
        _defaults = defaults;
        _written = WrittenAt();
        _settings = Read() ?? defaults;
    }

    public T Get()
    {
        lock (_gate)
        {
            var written = WrittenAt();
            if (written != _written)
            {
                // Gone, the defaults stand again; read, what it says. Caught
                // half-written by whoever is writing it, the last settings
                // stand and it is read on the next call, rather than the
                // defaults for a moment (auto-update on, say).
                if (written is null)
                {
                    _written = null;
                    _settings = _defaults;
                }
                else if (Read() is { } read)
                {
                    _written = written;
                    _settings = read;
                }
            }

            return _settings;
        }
    }

    public T Set(T settings)
    {
        lock (_gate)
        {
            _settings = settings;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_settings, JsonOptions));
            _written = WrittenAt();
            return _settings;
        }
    }

    /// <summary>When the file was last written; null while there is none, or it cannot be looked at.</summary>
    private DateTime? WrittenAt()
    {
        try
        {
            return File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return _written;
        }
    }

    /// <summary>A file written by hand, or half-written, must not stop the agent: the defaults stand.</summary>
    private T? Read()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(_path), JsonOptions) : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
