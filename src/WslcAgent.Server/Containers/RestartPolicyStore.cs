using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// The agent-owned restart policy: not a CLI flag
/// but a file of enrolled containers (<c>unless-stopped</c> or <c>always</c>)
/// with the state the user last asked for. <c>no</c> is never stored. Keyed by
/// the container name (or a full id when it has none), looked up by name, id
/// or either's 12-character prefix.
/// </summary>
public sealed partial class RestartPolicyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly ILogger<RestartPolicyStore> _logger;
    private readonly Lock _gate = new();
    private Dictionary<string, Entry> _entries;

    public RestartPolicyStore(IOptions<WslcOptions> options, ILogger<RestartPolicyStore> logger)
    {
        _logger = logger;
        _path = Path.Combine(options.Value.DataDirectory, "restart-policies.json");
        _entries = Load();
    }

    public RestartPolicyInfo Get(string nameOrId)
    {
        lock (_gate)
        {
            return Find(nameOrId) is { } entry
                ? new RestartPolicyInfo(entry.Policy, entry.Desired, true)
                : RestartPolicyInfo.None;
        }
    }

    /// <summary>Enrols or unenrols after a create, run, recreate or an explicit PUT.</summary>
    public void Set(string name, string id, string policy, string desired)
    {
        if (!RestartPolicyInfo.IsKnown(policy))
        {
            throw new ArgumentException($"Unknown restart policy: {policy}", nameof(policy));
        }

        lock (_gate)
        {
            var key = Key(name, id);
            if (key is null)
            {
                return;
            }

            foreach (var stale in _entries.Where(e => Matches(e.Key, e.Value, name) || Matches(e.Key, e.Value, id)).Select(e => e.Key).ToList())
            {
                _entries.Remove(stale);
            }

            if (policy != RestartPolicyInfo.No)
            {
                _entries[key] = new Entry(policy, desired, id, DateTimeOffset.UtcNow);
            }

            Save();
        }
    }

    /// <summary>A start or stop by the user changes what the policy should keep.</summary>
    public void SetDesired(string nameOrId, string desired)
    {
        lock (_gate)
        {
            var match = _entries.FirstOrDefault(e => Matches(e.Key, e.Value, nameOrId));
            if (match.Key is null)
            {
                return;
            }

            _entries[match.Key] = match.Value with { Desired = desired, UpdatedAt = DateTimeOffset.UtcNow };
            Save();
        }
    }

    public void Remove(string nameOrId)
    {
        lock (_gate)
        {
            var keys = _entries.Where(e => Matches(e.Key, e.Value, nameOrId)).Select(e => e.Key).ToList();
            if (keys.Count == 0)
            {
                return;
            }

            foreach (var key in keys)
            {
                _entries.Remove(key);
            }

            Save();
        }
    }

    /// <summary>The policy of every listed container, for the Restart column.</summary>
    public IReadOnlyList<ContainerSummary> Annotate(IReadOnlyList<ContainerSummary> containers)
    {
        lock (_gate)
        {
            return containers.Select(c => c with { RestartPolicy = Find(c.Name)?.Policy ?? Find(c.Id)?.Policy ?? "" }).ToList();
        }
    }

    /// <summary>What the start-up reconcile starts: <c>always</c> unconditionally, <c>unless-stopped</c> only when last asked to run.</summary>
    public IReadOnlyList<string> KeysToStart()
    {
        lock (_gate)
        {
            return _entries
                .Where(e => e.Value.Policy == RestartPolicyInfo.Always || e.Value.Desired == RestartPolicyInfo.Running)
                .Select(e => e.Key)
                .ToList();
        }
    }

    private Entry? Find(string nameOrId)
    {
        var match = _entries.FirstOrDefault(e => Matches(e.Key, e.Value, nameOrId));
        return match.Key is null ? null : match.Value;
    }

    private static bool Matches(string key, Entry entry, string nameOrId)
    {
        var text = nameOrId.Trim().TrimStart('/');
        if (text.Length == 0)
        {
            return false;
        }

        return SameOrPrefix(key, text) || SameOrPrefix(entry.Id, text);
    }

    private static bool SameOrPrefix(string stored, string given)
    {
        if (stored.Length == 0)
        {
            return false;
        }

        if (stored.Equals(given, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return HexId().IsMatch(stored) && HexId().IsMatch(given)
            && (stored.StartsWith(given, StringComparison.OrdinalIgnoreCase) || given.StartsWith(stored, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The name when it is a valid one, else the full id, else nothing to enrol.</summary>
    private static string? Key(string name, string id)
    {
        var candidate = name.Trim().TrimStart('/');
        if (ValidName().IsMatch(candidate))
        {
            return candidate;
        }

        return HexId().IsMatch(id.Trim()) ? id.Trim().ToLowerInvariant() : null;
    }

    private Dictionary<string, Entry> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var file = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(_path));
                return file?.Containers?.Where(e => RestartPolicyInfo.IsKnown(e.Value.Policy) && e.Value.Policy != RestartPolicyInfo.No)
                    .ToDictionary(e => e.Key, e => e.Value) ?? new Dictionary<string, Entry>();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning("restart policies unreadable at {Path}: {Message}", _path, ex.Message);
        }

        return new Dictionary<string, Entry>();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new StoreFile(1, _entries), JsonOptions));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("restart policies not saved to {Path}: {Message}", _path, ex.Message);
        }
    }

    private sealed record StoreFile(int Version, Dictionary<string, Entry> Containers);

    private sealed record Entry(string Policy, string Desired, string Id, DateTimeOffset UpdatedAt);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$")]
    private static partial Regex ValidName();

    [GeneratedRegex("^[0-9a-fA-F]{12,64}$")]
    private static partial Regex HexId();
}
