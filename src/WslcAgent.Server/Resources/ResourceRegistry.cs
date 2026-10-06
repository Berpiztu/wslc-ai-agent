using System.Text.Json;
using Microsoft.Extensions.Options;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Resources;

/// <summary>
/// The one place that says which resource is which:
/// every container, image, volume and network the agent has seen,
/// each with a number of its own — its uid — that no rename and no recreate
/// changes, kept in <c>resources.json</c> in the agent's data folder. What the
/// resource is called and which WSLC id it has now are read from here; the
/// dashboard keeps only the uid, so a card stays on its resource through both. A resource that is removed takes its
/// uid with it, and a card that pointed at it is not drawn any more.
///
/// It is kept current two ways. The agent's own verbs say what they did: a
/// removal drops the entry at once, a recreate keeps its entry through the
/// moment the container does not exist and then gives it the new id and, if
/// it changed, the new name. Whatever changes behind the agent's back — the
/// CLI, by hand — is found on every full read of the list: an id it knows
/// takes the name it has now; an id it does not know, with the name of one
/// that is gone, is that one recreated; anything else is new; and what is no
/// longer there is dropped. The entries carry their kind, and the WSLC session
/// they live in: the agent can be switched to another session, whose resources are
/// others, and a read there must not take this session's uids away.
///
/// The file is not this process's alone: an installed agent and a development
/// one on the same machine share the data folder, and each kept the copy it
/// read when it started and wrote it back whole, so each took away what the
/// other had entered — a container the other knew got a new uid, and the
/// dashboard's cards lost it. Every change is made under a lock on the file
/// that both take (<see cref="Change"/>), on the file as it is now, and a read
/// takes the file again when another process has written it since.
/// </summary>
public sealed class ResourceRegistry
{
    /// <summary>The kind every container entry carries: its WSLC id and its name.</summary>
    public const string Container = "container";

    /// <summary>An image, by its <c>repository:tag</c>: a new pull of the tag is the same entry, a new tag another; a dangling image has none.</summary>
    public const string Image = "image";

    /// <summary>A volume, by its name, which is all a volume has.</summary>
    public const string Volume = "volume";

    /// <summary>A network, by its WSLC id and its name, as a container: a recreate can change both.</summary>
    public const string Network = "network";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>How long a change waits for another process to let go of the file before it goes on without the lock.</summary>
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(3);

    private readonly string _path;
    private readonly string _lockPath;

    /// <summary>When the file read last was written: another process writing it since makes a read take it again.</summary>
    private DateTime _readWritten;
    private readonly ISelectedSession _session;
    private readonly ILogger<ResourceRegistry> _logger;
    private readonly Lock _gate = new();
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);
    private Stored _stored;

    public ResourceRegistry(IOptions<WslcOptions> options, ISelectedSession session, ILogger<ResourceRegistry> logger)
    {
        _session = session;
        _logger = logger;
        _path = Path.Combine(options.Value.DataDirectory, "resources.json");
        _lockPath = _path + ".lock";
        _stored = Load();
    }

    /// <summary>One resource: its uid, its kind, the WSLC session it lives in, and its WSLC id and name as they are now.</summary>
    private sealed record Entry(int Uid, string Kind, string Session, string WslcId, string Name);

    /// <summary>The entries and the uid the next one takes.</summary>
    private sealed record Stored(int Next, List<Entry> Entries);

    /// <summary>
    /// The uid of each resource read, reconciled with what is known: the
    /// resources are the whole list of their kind when <paramref name="complete"/>
    /// (a list of the running ones alone says nothing about the stopped ones,
    /// and drops nothing).
    /// </summary>
    public IReadOnlyDictionary<string, int> Reconcile(string kind, IReadOnlyList<(string WslcId, string Name)> resources, bool complete)
    {
        var uids = new Dictionary<string, int>(StringComparer.Ordinal);
        Change(() =>
        {
            uids.Clear();
            var changed = false;
            var seen = new HashSet<int>();
            foreach (var (wslcId, name) in resources)
            {
                var entry = Of(kind).FirstOrDefault(e => SameId(e.WslcId, wslcId))
                    ?? Of(kind).FirstOrDefault(e => e.Name == name && !seen.Contains(e.Uid) && resources.All(r => !SameId(r.WslcId, e.WslcId)));
                if (entry is null)
                {
                    entry = new Entry(_stored.Next, kind, _session.Name, wslcId, name);
                    _stored = _stored with { Next = _stored.Next + 1 };
                    _stored.Entries.Add(entry);
                    changed = true;
                }
                else if (entry.WslcId != wslcId || entry.Name != name)
                {
                    entry = Replace(entry, entry with { WslcId = wslcId, Name = name });
                    changed = true;
                }

                seen.Add(entry.Uid);
                uids[wslcId] = entry.Uid;
            }

            if (complete)
            {
                changed |= _stored.Entries.RemoveAll(e => e.Kind == kind && e.Session == _session.Name && !seen.Contains(e.Uid) && !_held.Any(held => SameId(held, e.WslcId))) > 0;
            }

            return changed;
        });

        return uids;
    }

    /// <summary>
    /// What a resource is called, by its WSLC id or its name, as the last list
    /// read it; what was asked for when it is not known. For a line a person
    /// reads — a transfer's title in the log — where a client named the
    /// container by its id.
    /// </summary>
    public string NameOf(string kind, string wslcIdOrName)
    {
        lock (_gate)
        {
            Refresh();
            return (Of(kind).FirstOrDefault(e => SameId(e.WslcId, wslcIdOrName)) ?? Of(kind).FirstOrDefault(e => e.Name == wslcIdOrName))?.Name
                is { Length: > 0 } name ? name : wslcIdOrName;
        }
    }

    /// <summary>A resource by its uid, in the selected session: its kind and its name; null when it is not known.</summary>
    public (string Kind, string Name)? Describe(int uid)
    {
        lock (_gate)
        {
            Refresh();
            return _stored.Entries.FirstOrDefault(e => e.Uid == uid && e.Session == _session.Name) is { } entry ? (entry.Kind, entry.Name) : null;
        }
    }

    /// <summary>The uid of one resource, by its WSLC id (either may be the short or the full form) or, failing that, its name; 0 when it is not known.</summary>
    public int UidOf(string kind, string wslcId, string name)
    {
        lock (_gate)
        {
            Refresh();
            return (Of(kind).FirstOrDefault(e => SameId(e.WslcId, wslcId)) ?? Of(kind).FirstOrDefault(e => e.Name == name))?.Uid ?? 0;
        }
    }

    /// <summary>A recreate is about to remove the resource: its entry outlives the moment it does not exist.</summary>
    public void Hold(string wslcId)
    {
        lock (_gate)
        {
            _held.Add(wslcId);
        }
    }

    /// <summary>The recreate is over, done or undone: the entry is no longer kept past its resource.</summary>
    public void Release(string wslcId)
    {
        lock (_gate)
        {
            _held.Remove(wslcId);
        }
    }

    /// <summary>
    /// A recreate made the resource again under <paramref name="newId"/>, and
    /// maybe a new name: its entry — the uid a card points at — takes both. A
    /// read that ran in between may have entered the new resource as a new one;
    /// that entry goes, so the resource keeps the uid it had.
    /// </summary>
    public void Recreated(string kind, string oldId, string newId, string newName) =>
        Change(() =>
        {
            var entry = Of(kind).FirstOrDefault(e => SameId(e.WslcId, oldId));
            if (entry is null)
            {
                return false;
            }

            _stored.Entries.RemoveAll(e => e.Kind == kind && e.Session == _session.Name && e.Uid != entry.Uid && SameId(e.WslcId, newId));
            Replace(entry, entry with { WslcId = newId, Name = newName.Length > 0 ? newName : entry.Name });
            return true;
        });

    /// <summary>The agent removed the resource: its uid goes with it.</summary>
    public void Removed(string kind, string wslcIdOrName) =>
        Change(() => _stored.Entries.RemoveAll(e => e.Kind == kind && e.Session == _session.Name && (SameId(e.WslcId, wslcIdOrName) || e.Name == wslcIdOrName)) > 0);

    /// <summary>
    /// One change, made on the file as it is now: under the lock on the file
    /// every process takes, the file read again if another process wrote it
    /// since (<see cref="Refresh"/>), <paramref name="apply"/> run on
    /// it, and the file written when it says it changed something. A file
    /// another process holds for longer than <see cref="LockWait"/> is not
    /// waited for: the change is made on what this process knows, as before.
    /// </summary>
    private void Change(Func<bool> apply)
    {
        lock (_gate)
        {
            using var held = LockFile();
            Refresh();
            if (apply())
            {
                Save();
            }
        }
    }

    /// <summary>The file again, when another process has written it since it was read.</summary>
    private void Refresh()
    {
        if (Written() != _readWritten)
        {
            _stored = Load();
        }
    }

    private DateTime Written()
    {
        try
        {
            return File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return _readWritten;
        }
    }

    /// <summary>The lock on the file every process takes to change it: a file opened by one alone, held until disposed; null when it could not be had in time.</summary>
    private FileStream? LockFile()
    {
        var deadline = DateTime.UtcNow + LockWait;
        while (true)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_lockPath)!);
                return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("resources.json could not be locked: {Message}", exception.Message);
                return null;
            }
        }
    }

    /// <summary>The entries of a kind in the WSLC session the agent works in now: another session's resources are not these, and a read of this one says nothing about them.</summary>
    private IEnumerable<Entry> Of(string kind) => _stored.Entries.Where(e => e.Kind == kind && e.Session == _session.Name);

    private Entry Replace(Entry old, Entry updated)
    {
        _stored.Entries[_stored.Entries.IndexOf(old)] = updated;
        return updated;
    }

    /// <summary>
    /// The same resource: the same text, or — for WSLC's hexadecimal ids only,
    /// the list giving the 12-character one and inspect the full one — one the
    /// start of the other. Never a prefix of a name: <c>nginx:latest</c> is not
    /// <c>nginx:latest-alpine</c>.
    /// </summary>
    private static bool SameId(string a, string b) =>
        a.Length > 0 && b.Length > 0
        && (a == b || (IsHexId(a) && IsHexId(b) && (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal))));

    private static bool IsHexId(string text) => text.Length >= 12 && text.All(char.IsAsciiHexDigitLower);

    private void Save()
    {
        try
        {
            // Written beside it and put in its place whole: the other process
            // reads it without the lock, and must never find it half-written.
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var written = _path + ".tmp";
            File.WriteAllText(written, JsonSerializer.Serialize(_stored, Json));
            File.Move(written, _path, overwrite: true);
            _readWritten = Written();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The uids still hold for this run; the next change tries the file again.
            _logger.LogWarning("resources.json could not be written: {Message}", exception.Message);
        }
    }

    /// <summary>A file written by hand, or half-written, must not stop the agent: it starts again from uid 1, and the next read fills it.</summary>
    private Stored Load()
    {
        try
        {
            _readWritten = Written();
            if (File.Exists(_path) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(_path), Json) is { Entries: not null } stored)
            {
                return stored with
                {
                    Next = Math.Max(stored.Next, stored.Entries.Select(e => e.Uid).DefaultIfEmpty(0).Max() + 1),
                    Entries = stored.Entries.Select(Named).ToList(),
                };
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning("resources.json could not be read: {Message}", exception.Message);
        }

        return new Stored(1, []);
    }

    /// <summary>
    /// An entry written when the session the agent targets could have no name:
    /// no name meant the CLI's own store, so that is the name it takes now.
    /// Without this every resource in it would be a stranger and take a new
    /// uid — which is not a migration, it is renumbering the user's list.
    /// </summary>
    private static Entry Named(Entry entry) =>
        entry.Session.Length > 0 ? entry : entry with { Session = SessionStores.Default };
}
