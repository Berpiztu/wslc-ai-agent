using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Publishing;

/// <summary>
/// The wslc-published plugin (plugins/wslc-published), as the agent talks to it: its
/// root API on the PC's loopback, with the admin token it made in its data folder.
/// The plugin keeps the sites, writes and reloads nginx, checks whether each
/// application asks for a login of its own and keeps each name's users; the agent
/// only tells it what is published and reads back who may open it.
/// </summary>
public interface IPublishedPlugin
{
    /// <summary>The name published, or moved to another container or port: nginx reloaded, the check started.</summary>
    Task PutSiteAsync(Publication publication, CancellationToken cancellationToken = default);

    Task RemoveSiteAsync(string hostname, CancellationToken cancellationToken = default);

    /// <summary>Every name the plugin serves, with its access; never its users' names or passwords.</summary>
    Task<IReadOnlyList<PluginSite>> SitesAsync(CancellationToken cancellationToken = default);

    /// <summary>The name checked again now: whether its application asks for a login of its own.</summary>
    Task<PluginSite> CheckAsync(string hostname, CancellationToken cancellationToken = default);

    /// <summary>The proxy's login in front of the name even when its application has its own, or not.</summary>
    Task<PluginSite> ForceLoginAsync(string hostname, bool force, CancellationToken cancellationToken = default);

    /// <summary>A request to the plugin's own page or API, passed through as it came, with the token added.</summary>
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);
}

/// <summary>One name as the plugin reports it.</summary>
public sealed record PluginSite(string Host, string Container, int Port, string Access, string Check, string ClaimCode, IReadOnlyList<PluginUser> Users, bool ForceLogin = false);

/// <summary>A user of a name, without its password.</summary>
public sealed record PluginUser(string Name, string Role);

public sealed class PublishedPlugin(HttpClient http, PublishingSettingsStore settings) : IPublishedPlugin
{
    /// <summary>The plugin's root API and page, published on the PC's loopback only.</summary>
    public const int AdminPort = 8082;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task PutSiteAsync(Publication publication, CancellationToken cancellationToken = default) =>
        await CallAsync(HttpMethod.Put, $"api/sites/{Uri.EscapeDataString(publication.Hostname)}",
            new { container = publication.Container, port = publication.ContainerPort }, cancellationToken);

    public async Task RemoveSiteAsync(string hostname, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"api/sites/{Uri.EscapeDataString(hostname)}"), cancellationToken);
        // A name the plugin does not have is already where it should be.
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            throw await RefusedAsync(response, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<PluginSite>> SitesAsync(CancellationToken cancellationToken = default) =>
        await CallAsync<List<PluginSite>>(HttpMethod.Get, "api/sites", null, cancellationToken) ?? [];

    public async Task<PluginSite> CheckAsync(string hostname, CancellationToken cancellationToken = default) =>
        await CallAsync<PluginSite>(HttpMethod.Post, $"api/sites/{Uri.EscapeDataString(hostname)}/check", null, cancellationToken)
            ?? throw new InvalidOperationException("The publishing proxy gave no answer.");

    public async Task<PluginSite> ForceLoginAsync(string hostname, bool force, CancellationToken cancellationToken = default) =>
        await CallAsync<PluginSite>(HttpMethod.Put, $"api/sites/{Uri.EscapeDataString(hostname)}/force-login", new { force }, cancellationToken)
            ?? throw new InvalidOperationException("The publishing proxy gave no answer.");

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        // The page itself is the empty path, and a request made with an empty one has no URI at all.
        request.RequestUri = new Uri(new Uri($"http://127.0.0.1:{AdminPort}/"), (request.RequestUri?.OriginalString ?? "").TrimStart('/'));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"The publishing proxy does not answer at 127.0.0.1:{AdminPort} ({ex.Message}): run Set up in Settings → Publishing.");
        }
    }

    private async Task CallAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(Request(method, path, body), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await RefusedAsync(response, cancellationToken);
        }
    }

    private async Task<T?> CallAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(Request(method, path, body), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await RefusedAsync(response, cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, object? body) =>
        new(method, path) { Content = body is null ? null : JsonContent.Create(body, options: Json) };

    /// <summary>What the plugin said, as an error the endpoint maps: its own words when it gave some.</summary>
    private static async Task<Exception> RefusedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var said = text;
        try
        {
            said = JsonDocument.Parse(text).RootElement.GetProperty("error").GetString() ?? text;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Not its JSON: the text as it came.
        }

        return response.StatusCode == System.Net.HttpStatusCode.NotFound
            ? new KeyNotFoundException(said)
            : new InvalidOperationException($"The publishing proxy refused: {said}");
    }

    /// <summary>The token the plugin made in its data folder on its first start; read each time, so a new proxy's is picked up.</summary>
    private string Token()
    {
        var path = Path.Combine(settings.Get().DataFolder, "admin-token");
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"The publishing proxy's admin token is not at {path} ({ex.Message}): run Set up in Settings → Publishing.");
        }
    }
}
