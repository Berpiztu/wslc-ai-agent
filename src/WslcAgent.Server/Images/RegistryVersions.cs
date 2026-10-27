using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Images;

/// <summary>
/// The version an image not on the machine says it is, read from its registry
/// before it is pulled: Update image says which version a tag is before it
/// goes to it. The registry is asked as a pull asks it, without a login (the
/// anonymous token a public registry hands out), for the tag's manifest, the
/// one of this machine's platform when the tag holds several, and the image's
/// configuration it names; what that configuration says of its version is
/// read as a pulled image's is (<see cref="ImageVersions.Of"/>). A private
/// image's registry asks for a login, and that is said: its version is known
/// once it is pulled. An answer is kept a few minutes, by the reference.
/// </summary>
public sealed partial class RegistryVersions(HttpClient http, ILogger<RegistryVersions> logger)
{
    private static readonly TimeSpan KeptFor = TimeSpan.FromMinutes(10);

    /// <summary>The kinds of manifest a tag may hold, a list of platforms' or one image's, Docker's and OCI's.</summary>
    private static readonly string[] Manifests =
    [
        "application/vnd.oci.image.index.v1+json",
        "application/vnd.docker.distribution.manifest.list.v2+json",
        "application/vnd.oci.image.manifest.v1+json",
        "application/vnd.docker.distribution.manifest.v2+json",
    ];

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, RegistryImageVersion Answer)> _kept = new(StringComparer.OrdinalIgnoreCase);

    public async Task<RegistryImageVersion> OfAsync(string image, CancellationToken cancellationToken = default)
    {
        var reference = ImageReference.Normalize(image);
        if (_kept.TryGetValue(reference, out var kept) && DateTimeOffset.UtcNow - kept.At < KeptFor)
        {
            return kept.Answer;
        }

        var answer = await AskAsync(reference, cancellationToken);
        _kept[reference] = (DateTimeOffset.UtcNow, answer);
        return answer;
    }

    private async Task<RegistryImageVersion> AskAsync(string reference, CancellationToken cancellationToken)
    {
        RegistryImageVersion Said(string note) => new(reference, "", "", note);
        var (host, repository, tag) = Where(reference);
        try
        {
            var token = (string?)null;
            var manifest = await GetAsync($"https://{host}/v2/{repository}/manifests/{tag}", Manifests, null, cancellationToken);
            if (manifest.StatusCode == HttpStatusCode.Unauthorized)
            {
                token = await TokenAsync(manifest, repository, cancellationToken);
                manifest.Dispose();
                manifest = await GetAsync($"https://{host}/v2/{repository}/manifests/{tag}", Manifests, token, cancellationToken);
            }

            using (manifest)
            {
                if (manifest.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return Said("known once it is pulled (the registry asks for a login)");
                }

                if (manifest.StatusCode == HttpStatusCode.NotFound)
                {
                    return Said("the registry has no such tag");
                }

                manifest.EnsureSuccessStatusCode();
                var config = await ConfigDigestAsync(host, repository, token, await ReadJsonAsync(manifest, cancellationToken), cancellationToken);
                if (config.Length == 0)
                {
                    return Said("the registry did not say");
                }

                using var blob = await GetAsync($"https://{host}/v2/{repository}/blobs/{config}", ["application/json", "*/*"], token, cancellationToken);
                blob.EnsureSuccessStatusCode();
                var version = ImageVersions.Of(await ReadJsonAsync(blob, cancellationToken));
                return version.Version.Length > 0
                    ? new RegistryImageVersion(reference, version.Version, version.From, "")
                    : Said("the image does not say one");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            logger.LogInformation("the registry of {Image} could not say its version: {Message}", reference, ex.Message);
            return Said("the registry could not be asked");
        }
    }

    /// <summary>
    /// The image's configuration a manifest names: one image's, at once; a list of platforms', the one of
    /// this machine's (its manifest read in turn), the attestations a list also carries left aside.
    /// </summary>
    private async Task<string> ConfigDigestAsync(string host, string repository, string? token, JsonElement manifest, CancellationToken cancellationToken)
    {
        if (manifest.TryGetProperty("config", out var config) && config.TryGetProperty("digest", out var digest))
        {
            return digest.GetString() ?? "";
        }

        if (!manifest.TryGetProperty("manifests", out var platforms) || platforms.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        var architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
        var chosen = platforms.EnumerateArray()
            .Where(entry => entry.TryGetProperty("platform", out var platform)
                && platform.TryGetProperty("os", out var os) && os.GetString() == "linux"
                && platform.TryGetProperty("architecture", out var arch) && arch.GetString() == architecture)
            .Select(entry => entry.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "")
            .FirstOrDefault(d => d.Length > 0);
        if (chosen is null)
        {
            return "";
        }

        using var one = await GetAsync($"https://{host}/v2/{repository}/manifests/{chosen}", Manifests, token, cancellationToken);
        one.EnsureSuccessStatusCode();
        var image = await ReadJsonAsync(one, cancellationToken);
        return image.TryGetProperty("config", out var c) && c.TryGetProperty("digest", out var cd) ? cd.GetString() ?? "" : "";
    }

    /// <summary>
    /// The anonymous token the registry's answer points to (<c>WWW-Authenticate: Bearer realm=…,service=…</c>),
    /// for pulling this repository: what a pull without a login is handed by a public registry; null when it names none.
    /// </summary>
    private async Task<string?> TokenAsync(HttpResponseMessage refused, string repository, CancellationToken cancellationToken)
    {
        var challenge = refused.Headers.WwwAuthenticate.FirstOrDefault(header => header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))?.Parameter ?? "";
        var parts = ChallengePart().Matches(challenge).ToDictionary(match => match.Groups["key"].Value, match => match.Groups["value"].Value, StringComparer.OrdinalIgnoreCase);
        if (!parts.TryGetValue("realm", out var realm))
        {
            return null;
        }

        var query = $"scope={Uri.EscapeDataString($"repository:{repository}:pull")}"
            + (parts.TryGetValue("service", out var service) ? $"&service={Uri.EscapeDataString(service)}" : "");
        using var response = await http.GetAsync($"{realm}{(realm.Contains('?') ? '&' : '?')}{query}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await ReadJsonAsync(response, cancellationToken);
        return body.TryGetProperty("token", out var token) ? token.GetString()
            : body.TryGetProperty("access_token", out var access) ? access.GetString()
            : null;
    }

    private Task<HttpResponseMessage> GetAsync(string url, IEnumerable<string> accept, string? token, CancellationToken cancellationToken)
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

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
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

    [GeneratedRegex(@"(?<key>\w+)=""(?<value>[^""]*)""")]
    private static partial Regex ChallengePart();
}
