using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Images;

/// <summary>
/// The version an image not on the machine says it is, read from its registry
/// before it is pulled: Update image says which version a tag is before it
/// goes to it. The registry is asked as wslc's pull asks it
/// (<see cref="RegistryClient"/>), for the tag's manifest, the one of this
/// machine's platform when the tag holds several, and the image's
/// configuration it names; what that configuration says of its version is
/// read as a pulled image's is (<see cref="ImageVersions.Of"/>). A private
/// image whose registry wslc has no login for asks for one, and that is said:
/// its version is known once it is pulled. An answer is kept a few minutes,
/// by the reference.
/// </summary>
public sealed class RegistryVersions(RegistryClient registry, ILogger<RegistryVersions> logger)
{
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
        if (_kept.TryGetValue(reference, out var kept) && DateTimeOffset.UtcNow - kept.At < RegistryClient.KeptFor)
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
        var (host, repository, tag) = RegistryClient.Where(reference);
        try
        {
            var (manifest, token, login) = await registry.GetPullingAsync(host, $"https://{host}/v2/{repository}/manifests/{tag}", Manifests, repository, cancellationToken);
            using (manifest)
            {
                if (manifest.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return Said(login ? RegistryClient.RefusedLogin : "known once it is pulled (the registry asks for a login)");
                }

                if (manifest.StatusCode == HttpStatusCode.NotFound)
                {
                    return Said("the registry has no such tag");
                }

                manifest.EnsureSuccessStatusCode();
                var config = await ConfigDigestAsync(host, repository, token, await RegistryClient.ReadJsonAsync(manifest, cancellationToken), cancellationToken);
                if (config.Length == 0)
                {
                    return Said("the registry did not say");
                }

                using var blob = await registry.GetAsync($"https://{host}/v2/{repository}/blobs/{config}", ["application/json", "*/*"], token, cancellationToken);
                blob.EnsureSuccessStatusCode();
                var version = ImageVersions.Of(await RegistryClient.ReadJsonAsync(blob, cancellationToken));
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

        using var one = await registry.GetAsync($"https://{host}/v2/{repository}/manifests/{chosen}", Manifests, token, cancellationToken);
        one.EnsureSuccessStatusCode();
        var image = await RegistryClient.ReadJsonAsync(one, cancellationToken);
        return image.TryGetProperty("config", out var c) && c.TryGetProperty("digest", out var cd) ? cd.GetString() ?? "" : "";
    }
}
