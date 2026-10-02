using System.Text.Json;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Registries;

/// <summary>
/// The registries this agent logged in to, in <c>registry-logins.json</c> in its data
/// folder: the server, the user and when. <c>wslc</c> keeps the credentials and has no
/// command that lists them, so this is the agent's own record of the logins made
/// through it; a login typed at the console is not in it. Never the password.
/// </summary>
public sealed class RegistryLogins
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly Lock _gate = new();
    private List<RegistryLogin> _entries;

    public RegistryLogins(IOptions<WslcOptions> options)
    {
        _path = Path.Combine(options.Value.DataDirectory, "registry-logins.json");
        _entries = Load();
    }

    public IReadOnlyList<RegistryLogin> List()
    {
        lock (_gate)
        {
            return _entries.OrderBy(e => e.Server, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>A login that succeeded; a second one to the same server replaces the first, as <c>wslc</c> does.</summary>
    public void Record(string server, string username)
    {
        var key = KeyOf(server);
        lock (_gate)
        {
            _entries.RemoveAll(e => e.Server == key);
            _entries.Add(new RegistryLogin(key, username.Trim(), DateTimeOffset.UtcNow));
            Save();
        }
    }

    public void Forget(string server)
    {
        var key = KeyOf(server);
        lock (_gate)
        {
            if (_entries.RemoveAll(e => e.Server == key) > 0)
            {
                Save();
            }
        }
    }

    /// <summary>Registry hosts are not case-sensitive; empty stands for the session's default registry.</summary>
    private static string KeyOf(string server) => server.Trim().ToLowerInvariant();

    private List<RegistryLogin> Load() =>
        File.Exists(_path) ? JsonSerializer.Deserialize<List<RegistryLogin>>(File.ReadAllText(_path), JsonOptions) ?? [] : [];

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_entries, JsonOptions));
    }
}
