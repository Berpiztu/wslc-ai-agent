using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WslcAgent.ApiClient;
using WslcAgent.Server.Registries;

namespace WslcAgent.Server.Images;

/// <summary>
/// A registry asked as wslc's pull asks it, for what a pull may read: at
/// once, and when it answers 401, with the token its answer points to, got
/// with the login <c>wslc login</c> kept for it (<see cref="WslcCredentials"/>)
/// or else without one (the anonymous token a public registry hands out).
/// <see cref="RegistryVersions"/> and <see cref="RegistryTags"/> ask through it.
/// </summary>
public sealed partial class RegistryClient(HttpClient http)
{
    /// <summary>How long an answer of the registry is kept before it is asked again.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromMinutes(10);

    /// <summary>What is said when the registry refuses what wslc's login got.</summary>
    public const string RefusedLogin = "the registry refused the login wslc keeps for it";

    /// <summary>
    /// Asks the registry as a pull does: at once, and when it answers 401, again with the token its
    /// answer points to. The answer, the token for what follows (null when none was needed) and
    /// whether wslc's login got it.
    /// </summary>
    public async Task<(HttpResponseMessage Response, string? Token, bool Login)> GetPullingAsync(
        string host, string url, IEnumerable<string> accept, string repository, CancellationToken cancellationToken)
    {
        var response = await GetAsync(url, accept, null, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return (response, null, false);
        }

        var (token, login) = await TokenAsync(response, host, repository, cancellationToken);
        response.Dispose();
        return (await GetAsync(url, accept, token, cancellationToken), token, login);
    }

    public Task<HttpResponseMessage> GetAsync(string url, IEnumerable<string> accept, string? token, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var type in accept)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(type));
        }

        if (token is { Length: > 0 })
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return http.SendAsync(request, cancellationToken);
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// The registry's host, the repository on it and the tag (or digest): a first part with a dot, a colon or
    /// <c>localhost</c> is a registry; anything else is Docker Hub's, whose own images live under <c>library/</c>.
    /// </summary>
    internal static (string Host, string Repository, string Tag) Where(string reference)
    {
        var at = reference.IndexOf('@');
        var (repository, tag) = at >= 0
            ? (ImageReference.RepositoryOf(reference[..at]), reference[(at + 1)..])
            : ImageReference.Split(reference);
        var slash = repository.IndexOf('/');
        var first = slash < 0 ? "" : repository[..slash];
        if (first.Contains('.') || first.Contains(':') || first == "localhost")
        {
            return (first, repository[(slash + 1)..], tag);
        }

        return ("registry-1.docker.io", slash < 0 ? $"library/{repository}" : repository, tag);
    }

    /// <summary>
    /// The token the registry's answer points to (<c>WWW-Authenticate: Bearer realm=…,service=…</c>) for
    /// pulling this repository: with the login wslc keeps for the registry, as wslc's pull gets it (the
    /// user and password, or an identity token traded for one), and when there is none or it is refused,
    /// the anonymous one a public registry hands out. Null when the answer names no realm; whether the
    /// login got it.
    /// </summary>
    private async Task<(string? Token, bool Login)> TokenAsync(HttpResponseMessage refused, string host, string repository, CancellationToken cancellationToken)
    {
        var challenge = refused.Headers.WwwAuthenticate.FirstOrDefault(header => header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))?.Parameter ?? "";
        var parts = ChallengePart().Matches(challenge).ToDictionary(match => match.Groups["key"].Value, match => match.Groups["value"].Value, StringComparer.OrdinalIgnoreCase);
        if (!parts.TryGetValue("realm", out var realm))
        {
            return (null, false);
        }

        var scope = $"repository:{repository}:pull";
        var service = parts.GetValueOrDefault("service", "");
        if (WslcCredentials.For(host) is { } login && await TokenWithLoginAsync(realm, service, scope, login, cancellationToken) is { } token)
        {
            return (token, true);
        }

        using var anonymous = await http.GetAsync(TokenUrl(realm, service, scope), cancellationToken);
        return (await TokenOfAsync(anonymous, cancellationToken), false);
    }

    /// <summary>
    /// The token got with wslc's login: an identity token traded at the realm (OAuth2's refresh-token
    /// grant, as Docker's clients trade one), or the user and password sent to it; null when refused.
    /// </summary>
    private async Task<string?> TokenWithLoginAsync(string realm, string service, string scope, (string User, string Secret) login, CancellationToken cancellationToken)
    {
        using var request = login.User == WslcCredentials.TokenUser
            ? new HttpRequestMessage(HttpMethod.Post, realm)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = login.Secret,
                    ["service"] = service,
                    ["scope"] = scope,
                    ["client_id"] = "wslc-ai-agent",
                }),
            }
            : new HttpRequestMessage(HttpMethod.Get, TokenUrl(realm, service, scope))
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login.User}:{login.Secret}"))) },
            };
        using var response = await http.SendAsync(request, cancellationToken);
        return await TokenOfAsync(response, cancellationToken);
    }

    private static string TokenUrl(string realm, string service, string scope) =>
        $"{realm}{(realm.Contains('?') ? '&' : '?')}scope={Uri.EscapeDataString(scope)}"
        + (service.Length > 0 ? $"&service={Uri.EscapeDataString(service)}" : "");

    /// <summary>The token a realm handed out (<c>token</c>, or OAuth2's <c>access_token</c>); null when it refused.</summary>
    private static async Task<string?> TokenOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await ReadJsonAsync(response, cancellationToken);
        return body.TryGetProperty("token", out var token) ? token.GetString()
            : body.TryGetProperty("access_token", out var access) ? access.GetString()
            : null;
    }

    [GeneratedRegex(@"(?<key>\w+)=""(?<value>[^""]*)""")]
    private static partial Regex ChallengePart();
}
