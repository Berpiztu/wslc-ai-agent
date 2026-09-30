using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Auth;

/// <summary>
/// The agent's own login: one username and password
/// for whoever reaches the agent from anywhere but its own machine, an optional
/// API token for scripts and AI agents, and the signed session value a sign-in
/// hands out. Kept in <c>login.json</c> in the agent's data folder; the password
/// only as a salted PBKDF2 hash. Changing the password renews the signing key,
/// so every session issued before it ends.
/// </summary>
public sealed class AgentLogin
{
    public const string DefaultUsername = "admin";

    /// <summary>How long a session lasts from sign-in.</summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    private const int HashIterations = 100_000;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private Stored _stored;

    public AgentLogin(IOptions<WslcOptions> options, TimeProvider clock)
    {
        _clock = clock;
        _path = Path.Combine(options.Value.DataDirectory, "login.json");
        _stored = Load();
    }

    public string Username
    {
        get
        {
            lock (_gate)
            {
                return _stored.Username;
            }
        }
    }

    /// <summary>A password has been set: sign-in from outside is possible.</summary>
    public bool IsConfigured
    {
        get
        {
            lock (_gate)
            {
                return _stored.PasswordHash.Length > 0;
            }
        }
    }

    public bool HasApiToken
    {
        get
        {
            lock (_gate)
            {
                return _stored.ApiToken.Length > 0;
            }
        }
    }

    public bool CredentialsMatch(string username, string password)
    {
        Stored stored;
        lock (_gate)
        {
            stored = _stored;
        }

        if (stored.PasswordHash.Length == 0 || !SameText(username.Trim(), stored.Username))
        {
            return false;
        }

        var salt = Convert.FromBase64String(stored.PasswordSalt);
        return CryptographicOperations.FixedTimeEquals(Hash(password, salt), Convert.FromBase64String(stored.PasswordHash));
    }

    /// <summary>Sets the username and password; every session issued before ends.</summary>
    public void SetCredentials(string username, string password)
    {
        var name = username.Trim();
        if (name.Length == 0 || name.Contains(':'))
        {
            throw new ArgumentException("The username cannot be empty or contain ':'.", nameof(username));
        }

        if (password.Length == 0)
        {
            throw new ArgumentException("The password cannot be empty.", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        lock (_gate)
        {
            _stored = _stored with
            {
                Username = name,
                PasswordSalt = Convert.ToBase64String(salt),
                PasswordHash = Convert.ToBase64String(Hash(password, salt)),
                SessionSecret = NewSecret(),
            };
            Save();
        }
    }

    /// <summary>A new API token, shown once; the previous one stops working.</summary>
    public string RenewApiToken()
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        lock (_gate)
        {
            _stored = _stored with { ApiToken = token };
            Save();
        }

        return token;
    }

    public bool ApiTokenMatches(string token)
    {
        lock (_gate)
        {
            return _stored.ApiToken.Length > 0 && SameText(token, _stored.ApiToken);
        }
    }

    /// <summary><c>username:issued:signature</c>, the value of the session cookie and of a native client's bearer token.</summary>
    public string IssueSession()
    {
        lock (_gate)
        {
            var payload = $"{_stored.Username}:{_clock.GetUtcNow().ToUnixTimeSeconds()}";
            return $"{payload}:{Sign(payload, _stored.SessionSecret)}";
        }
    }

    /// <summary>The username of a session value that is signed, unexpired and still names the configured user; null otherwise.</summary>
    public string? SessionUser(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Count(c => c == ':') < 2)
        {
            return null;
        }

        var last = value.LastIndexOf(':');
        var middle = value.LastIndexOf(':', last - 1);
        var username = value[..middle];
        if (!long.TryParse(value[(middle + 1)..last], out var issued) || issued <= 0)
        {
            return null;
        }

        var age = _clock.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(issued);
        if (age > SessionLifetime || age < -TimeSpan.FromMinutes(5))
        {
            return null;
        }

        lock (_gate)
        {
            var expected = Sign(value[..last], _stored.SessionSecret);
            return SameText(value[(last + 1)..], expected) && SameText(username, _stored.Username) ? username : null;
        }
    }

    private static string Sign(string payload, string secret) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Convert.FromBase64String(secret), Encoding.UTF8.GetBytes(payload)));

    private static byte[] Hash(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, HashIterations, HashAlgorithmName.SHA256, 32);

    /// <summary>Constant-time comparison of two texts of any length.</summary>
    private static bool SameText(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(left)), SHA256.HashData(Encoding.UTF8.GetBytes(right)));

    private static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private Stored Load()
    {
        if (File.Exists(_path) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(_path)) is { SessionSecret.Length: > 0 } stored)
        {
            return stored;
        }

        var fresh = new Stored(DefaultUsername, "", "", NewSecret(), "");
        _stored = fresh;
        Save();
        return fresh;
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_stored, JsonOptions));
    }

    private sealed record Stored(string Username, string PasswordSalt, string PasswordHash, string SessionSecret, string ApiToken);
}
