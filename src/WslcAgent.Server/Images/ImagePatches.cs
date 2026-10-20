using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// Zaindari's patch: an image whose system packages have a fix published,
/// built again on itself with them upgraded (<c>apk upgrade</c>,
/// <c>apt-get upgrade</c>), as a Windows update patches Windows, and tagged
/// <c>…-patched</c>; then the containers chosen are moved to it, each with
/// everything else it has, as an image update moves one. Python libraries are
/// not patched: raising one is a change to the program, its requirements'.
/// The build is an ordinary build job: its console, its cancel, its check, and
/// under Block a patch still vulnerable moves nothing.
/// </summary>
public sealed class ImagePatches(IWslcRunner wslc, IImageSecurity security, ImageBuilds builds, IContainerService containers, IOptions<WslcOptions> options)
{
    /// <summary>The build started; 409 for an image with nothing to patch.</summary>
    public async Task<BuildJob> StartAsync(PatchImageRequest request, CancellationToken cancellationToken = default) =>
        await StartIfAnyAsync(request, cancellationToken)
        ?? throw new InvalidOperationException($"{request.Image} has nothing to patch: no system package with a fix published, or no package manager in the image to install it with.");

    /// <summary>The build started; null, nothing done, for an image with nothing to patch.</summary>
    public async Task<BuildJob?> StartIfAnyAsync(PatchImageRequest request, CancellationToken cancellationToken = default)
    {
        var image = ImageReference.Normalize(WslcArgs.Require(request.Image, "image"));
        var report = await security.ReportAsync(image, cancellationToken);
        if (report.Patch.Length == 0)
        {
            return null;
        }

        var ecosystem = report.Packages.First(package => package.Ecosystem != Installed.PyPI).Ecosystem;
        var context = Path.Combine(options.Value.DataDirectory, "image-patches", string.Join("_", report.Patch.Split(Path.GetInvalidFileNameChars().Append(':').ToArray())));
        Directory.CreateDirectory(context);
        await File.WriteAllTextAsync(Path.Combine(context, "Dockerfile"), Dockerfile(image, ecosystem, await UserAsync(image, cancellationToken)), cancellationToken);
        var moved = (request.Containers ?? []).Where(container => container.Trim().Length > 0).Distinct(StringComparer.Ordinal).ToList();
        return builds.Start(new BuildImageRequest(context, report.Patch), (say, token) => MoveAsync(report.Patch, moved, say, token));
    }

    /// <summary>
    /// The patch an image would be given: its tag with <c>-patched</c> (a patch
    /// patched again keeps its own); empty when no system package of it has a
    /// fix published, or it has no package manager to install one with (a
    /// distroless image).
    /// </summary>
    internal static string PatchOf(string image, IReadOnlyCollection<Installed> packages, IEnumerable<ImageVulnerability> found)
    {
        var managed = packages.Any(package =>
            package.Ecosystem.StartsWith("Alpine:", StringComparison.Ordinal) && package.Name == "apk-tools"
            || package.Ecosystem.StartsWith("Debian:", StringComparison.Ordinal) && package.Name == "apt"
            || package.Ecosystem.StartsWith("Ubuntu:", StringComparison.Ordinal) && package.Name == "apt");
        if (!managed || !found.Any(v => v.Ecosystem != Installed.PyPI && v.FixedIn.Length > 0))
        {
            return "";
        }

        var (repository, tag) = ImageReference.Split(image);
        return ImageReference.IsPatched(image) ? $"{repository}:{tag}" : $"{repository}:{tag}{ImageReference.PatchedSuffix}";
    }

    /// <summary>
    /// The patch's Dockerfile: the image, its packages upgraded as root, and its
    /// own user given back, since an image that runs as another would otherwise
    /// be refused the upgrade.
    /// </summary>
    internal static string Dockerfile(string image, string ecosystem, string user)
    {
        var upgrade = ecosystem.StartsWith("Alpine:", StringComparison.Ordinal)
            ? "apk upgrade --no-cache"
            : "apt-get update && DEBIAN_FRONTEND=noninteractive apt-get upgrade -y && rm -rf /var/lib/apt/lists/*";
        var lines = new List<string> { $"FROM {image}", "USER root", $"RUN {upgrade}" };
        if (user.Length > 0 && user is not ("root" or "0" or "0:0"))
        {
            lines.Add($"USER {user}");
        }

        return string.Join('\n', lines) + "\n";
    }

    /// <summary>The user the image runs as (<c>Config.User</c>); empty for root, or when it cannot be read.</summary>
    private async Task<string> UserAsync(string image, CancellationToken cancellationToken)
    {
        try
        {
            var result = await wslc.RunAsync(["image", "inspect", image], cancellationToken: cancellationToken);
            var inspected = JsonNode.Parse(result.Stdout) switch
            {
                JsonArray array => array.FirstOrDefault(),
                var single => single,
            };
            return ((string?)inspected?["Config"]?["User"] ?? "").Trim();
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException or JsonException)
        {
            return "";
        }
    }

    /// <summary>
    /// Each container chosen, recreated on the patch with everything else it has
    /// and started if it was running; one that fails is said and the others go on,
    /// and the job ends in error naming it.
    /// </summary>
    private async Task MoveAsync(string patch, IReadOnlyList<string> moved, Action<string> say, CancellationToken cancellationToken)
    {
        var failed = new List<string>();
        foreach (var container in moved)
        {
            try
            {
                var details = await containers.DetailsAsync(container, cancellationToken);
                await containers.RecreateAsync(details.Id, details.Form with { Image = patch, Start = details.IsRunning }, cancellationToken);
                say($"INFO: {details.Name} now runs {patch}.");
            }
            catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException or ArgumentException or InvalidOperationException)
            {
                say($"ERROR: {container} was not moved to {patch}: {ex.Message}");
                failed.Add(container);
            }
        }

        if (failed.Count > 0)
        {
            throw new InvalidOperationException($"{patch} was built, but {string.Join(", ", failed)} {(failed.Count == 1 ? "was" : "were")} not moved to it.");
        }
    }
}
