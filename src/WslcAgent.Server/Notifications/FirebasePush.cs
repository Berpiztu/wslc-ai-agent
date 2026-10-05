using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Notifications;

/// <summary>
/// The agent's notifications pushed to the registered phones through Firebase
/// Cloud Messaging: the agent hands
/// each one to Google, which wakes the phone, whether or not the phone can
/// reach the agent.
/// <para>
/// It sends with a Firebase service account's key, the JSON Firebase's console
/// gives, kept as <c>firebase-service-account.json</c> in the agent's data
/// folder, where the installer puts it when it was built with one
/// (docs/developer/private-files.md) and where it can be copied by hand. Without
/// it nothing is sent. Signed here, with the RSA .NET has, rather than with
/// Google's library: one JWT exchanged for an hour's access token (OAuth 2.0
/// for service accounts), then one HTTP call per phone (FCM HTTP v1).
/// </para>
/// </summary>
public sealed class FirebasePush(IOptions<WslcOptions> options, NotificationDeviceStore devices, TimeProvider time, ILogger<FirebasePush> logger) : IDisposable
{
    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim _authorizing = new(1, 1);
    private (string Value, DateTimeOffset Until)? _access;

    /// <summary>Where the key is looked for.</summary>
    public string KeyFile { get; } = Path.Combine(options.Value.DataDirectory, "firebase-service-account.json");

    /// <summary>The Firebase project of the key, or null when there is no key to send with.</summary>
    public string? Project => ReadKey()?.ProjectId;

    /// <summary>Pushes a notification to every registered phone, in the background: the notification never waits for Google.</summary>
    public void Send(AgentNotification notification)
    {
        if (devices.Recipients() is not { Count: > 0 } recipients || ReadKey() is not { } key)
        {
            return;
        }

        _ = SendAsync(key, recipients, notification);
    }

    private async Task SendAsync(ServiceAccountKey key, IReadOnlyList<NotificationDeviceStore.Device> recipients, AgentNotification notification)
    {
        try
        {
            var access = await AccessAsync(key);
            foreach (var recipient in recipients)
            {
                await SendOneAsync(key, access, recipient, notification);
            }
        }
        catch (Exception failed) when (failed is HttpRequestException or TaskCanceledException or JsonException or CryptographicException or ArgumentException or InvalidOperationException)
        {
            logger.LogWarning("notification push: {Id} was not sent: {Message}", notification.Id, failed.Message);
        }
    }

    private async Task SendOneAsync(ServiceAccountKey key, string access, NotificationDeviceStore.Device recipient, AgentNotification notification)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(key.ProjectId)}/messages:send")
        {
            Content = JsonContent.Create(Message(recipient, notification)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var response = await _http.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var answer = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == HttpStatusCode.NotFound
            || answer.Contains("UNREGISTERED", StringComparison.Ordinal)
            || answer.Contains("SENDER_ID_MISMATCH", StringComparison.Ordinal))
        {
            // The app was uninstalled, or its data wiped, or the token belongs to
            // another Firebase project than the key's (the app was rebuilt for a
            // new one): that token will never answer again.
            devices.Forget(recipient.Token);
            logger.LogInformation("notification push: a device Firebase no longer knows was forgotten");
            return;
        }

        logger.LogWarning("notification push: {Id} was refused ({Status}): {Answer}", notification.Id, (int)response.StatusCode, answer);
    }

    /// <summary>
    /// The FCM v1 message: data alone, high priority, so it reaches the app
    /// whether it is in front or not, and the app draws the notification —
    /// Android draws one with a title of its own itself, and that one cannot
    /// carry a button (the update's Cancel).
    /// </summary>
    private static object Message(NotificationDeviceStore.Device recipient, AgentNotification notification) => new
    {
        message = new
        {
            token = recipient.Token,
            data = new Dictionary<string, string>
            {
                [NotificationData.Agent] = recipient.Agent ?? "",
                [NotificationData.Id] = notification.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [NotificationData.Kind] = notification.Kind,
                [NotificationData.Severity] = notification.Severity,
                [NotificationData.Title] = notification.Title,
                [NotificationData.Text] = notification.Text,
                [NotificationData.Link] = notification.Link,
                [NotificationData.Action] = notification.Action,
            },
            android = new { priority = "HIGH" },
        },
    };

    /// <summary>An access token for an hour, asked for again five minutes before it ends.</summary>
    private async Task<string> AccessAsync(ServiceAccountKey key)
    {
        await _authorizing.WaitAsync();
        try
        {
            var now = time.GetUtcNow();
            if (_access is { } held && held.Until - now > TimeSpan.FromMinutes(5))
            {
                return held.Value;
            }

            using var response = await _http.PostAsync(key.TokenUri, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = Assertion(key, now),
            }));
            var answer = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Google refused the key ({(int)response.StatusCode}): {answer}");
            }

            var granted = JsonSerializer.Deserialize<Granted>(answer, Json) ?? throw new JsonException("Google's answer had no access token.");
            _access = (granted.AccessToken, now.AddSeconds(granted.ExpiresIn));
            return granted.AccessToken;
        }
        finally
        {
            _authorizing.Release();
        }
    }

    /// <summary>The JWT the service account signs: who it is, what it asks for, for an hour.</summary>
    private static string Assertion(ServiceAccountKey key, DateTimeOffset now)
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = key.ClientEmail,
            scope = Scope,
            aud = key.TokenUri,
            iat = now.ToUnixTimeSeconds(),
            exp = now.AddHours(1).ToUnixTimeSeconds(),
        }));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(key.PrivateKey);
        var signature = rsa.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{header}.{claims}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The key, read at every use so one copied in works at once; null when there is none, or it is not one.</summary>
    private ServiceAccountKey? ReadKey()
    {
        try
        {
            return File.Exists(KeyFile)
                && JsonSerializer.Deserialize<ServiceAccountKey>(File.ReadAllText(KeyFile)) is { ProjectId.Length: > 0, ClientEmail.Length: > 0, PrivateKey.Length: > 0 } key
                ? key
                : null;
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _authorizing.Dispose();
    }

    /// <summary>What is read of the service account's JSON.</summary>
    private sealed record ServiceAccountKey(
        [property: JsonPropertyName("project_id")] string ProjectId,
        [property: JsonPropertyName("client_email")] string ClientEmail,
        [property: JsonPropertyName("private_key")] string PrivateKey,
        [property: JsonPropertyName("token_uri")] string? TokenUriGiven)
    {
        public string TokenUri => TokenUriGiven is { Length: > 0 } uri ? uri : "https://oauth2.googleapis.com/token";
    }

    /// <summary>Google's answer to the JWT: the access token and its seconds.</summary>
    private sealed record Granted(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
