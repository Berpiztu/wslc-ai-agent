using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.SavedLogins;

/// <summary>
/// The saved logins, in <c>saved-logins.json</c> in the agent's data folder. The
/// email, the user and the password are written encrypted (ASP.NET Core data protection,
/// its keys in the same folder and, on Windows, sealed to the account the agent
/// runs as), so the file alone gives nothing away. Titles are unique, ignoring
/// case: the browser pane picks a login by its title.
/// </summary>
public sealed class SavedLoginStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly IDataProtector _protector;
    private readonly Lock _gate = new();
    private List<Entry> _entries;

    public SavedLoginStore(IOptions<WslcOptions> options)
    {
        var directory = options.Value.DataDirectory;
        _path = Path.Combine(directory, "saved-logins.json");
        _protector = DataProtectionProvider
            .Create(new DirectoryInfo(Path.Combine(directory, "keys")), setup =>
            {
                setup.SetApplicationName("WSLC-AI-Agent");
                if (OperatingSystem.IsWindows())
                {
                    setup.ProtectKeysWithDpapi();
                }
            })
            .CreateProtector("SavedLogins");
        _entries = Load();
    }

    public IReadOnlyList<SavedLogin> List()
    {
        lock (_gate)
        {
            return _entries.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase).Select(Open).ToList();
        }
    }

    /// <summary>A new login; <see cref="SavedLoginConflictException"/> when the title is taken.</summary>
    public SavedLogin Add(SavedLoginInput input)
    {
        var title = TitleOf(input);
        lock (_gate)
        {
            EnsureFree(title, exceptId: null);
            var entry = Seal(Guid.NewGuid().ToString("N"), title, input);
            _entries.Add(entry);
            Save();
            return Open(entry);
        }
    }

    /// <summary>Replaces a login; null when there is none with that id.</summary>
    public SavedLogin? Update(string id, SavedLoginInput input)
    {
        var title = TitleOf(input);
        lock (_gate)
        {
            var index = _entries.FindIndex(e => e.Id == id);
            if (index < 0)
            {
                return null;
            }

            EnsureFree(title, exceptId: id);
            _entries[index] = Seal(id, title, input);
            Save();
            return Open(_entries[index]);
        }
    }

    public bool Remove(string id)
    {
        lock (_gate)
        {
            if (_entries.RemoveAll(e => e.Id == id) == 0)
            {
                return false;
            }

            Save();
            return true;
        }
    }

    private static string TitleOf(SavedLoginInput input) =>
        string.IsNullOrWhiteSpace(input.Title) ? throw new ArgumentException("A title is required.", nameof(input)) : input.Title.Trim();

    private void EnsureFree(string title, string? exceptId)
    {
        if (_entries.Any(e => e.Id != exceptId && string.Equals(e.Title, title, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SavedLoginConflictException($"A saved login titled \"{title}\" already exists.");
        }
    }

    private Entry Seal(string id, string title, SavedLoginInput input) =>
        new(id, title, _protector.Protect(input.User ?? ""), _protector.Protect(input.Password ?? ""), _protector.Protect(input.Email ?? ""));

    private SavedLogin Open(Entry entry) =>
        new(entry.Id, entry.Title, Reveal(entry.Email), Reveal(entry.User), Reveal(entry.Password));

    /// <summary>A field of a login written before it had one is empty, and empty is not something to unprotect.</summary>
    private string Reveal(string sealedText) =>
        string.IsNullOrEmpty(sealedText) ? "" : _protector.Unprotect(sealedText);

    private List<Entry> Load()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_path), JsonOptions) ?? [];
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_entries, JsonOptions));
    }

    /// <summary>A stored login: the title in the clear, the rest protected. Email came later, so a file without it reads as empty.</summary>
    private sealed record Entry(string Id, string Title, string User, string Password, string Email = "");
}

/// <summary>The title of a new or changed saved login is already used by another.</summary>
public sealed class SavedLoginConflictException(string message) : Exception(message);
