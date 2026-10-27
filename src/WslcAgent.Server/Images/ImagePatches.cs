using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Projects;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// Zaindari's patch: the image built again on itself, tagged <c>…-patched</c>,
/// with what its window asks. Its system packages upgraded (<c>apk upgrade</c>,
/// <c>apt-get upgrade</c>), as a Windows update patches Windows, when a fix is
/// published that the image does not carry yet; and the user's own commands,
/// a Python library raised to the version Zaindari names, anything the image
/// needs. Then the containers chosen are moved to it, each with everything else
/// it has, as an image update moves one; a project's too, kept on the patch at
/// its project's saves by <see cref="PatchedServices"/>, since its file names
/// the original. The build is an ordinary build job:
/// its console, its cancel, its check; a patch is never refused by the check,
/// since what is left in it is its original's to mend. If the patch does not
/// work, the container goes back to the image it had, by View &amp; edit.
/// </summary>
public sealed class ImagePatches(IWslcRunner wslc, IImageSecurity security, ImageBuilds builds, IContainerService containers, ImageMarks marks, PatchedServices patched, WslcEvents events, IOptions<WslcOptions> options)
{
    /// <summary>The build started; 409 for a patch with nothing to do.</summary>
    public async Task<BuildJob> StartAsync(PatchImageRequest request, CancellationToken cancellationToken = default) =>
        await StartIfAnyAsync(request, cancellationToken)
        ?? throw new InvalidOperationException($"The patch of {request.Image} has nothing to do: no system package to upgrade, and no command of yours.");

    /// <summary>The build started; null, nothing done, for a patch with nothing to do (no system upgrade asked or possible, no command).</summary>
    public async Task<BuildJob?> StartIfAnyAsync(PatchImageRequest request, CancellationToken cancellationToken = default)
    {
        var image = ImageReference.Normalize(WslcArgs.Require(request.Image, "image"));
        var report = await security.ReportAsync(image, cancellationToken);
        var upgrade = request.System ? report.SystemUpgrade : "";
        var commands = Commands(request.Commands);
        var written = (request.Dockerfile ?? "").Trim();
        if (!report.Found || (written.Length == 0 && upgrade.Length == 0 && commands.Count == 0))
        {
            return null;
        }

        // The patch window's Dockerfile is built as the user left it; the rest is written here from the choices.
        var (id, user) = await InspectAsync(image, cancellationToken);
        var dockerfile = written.Length > 0
            ? written.ReplaceLineEndings("\n") + "\n"
            : Dockerfile(image, upgrade, commands, user);
        var patch = Named(image);
        var context = Path.Combine(options.Value.DataDirectory, "image-patches", string.Join("_", patch.Split(Path.GetInvalidFileNameChars().Append(':').ToArray())));
        Directory.CreateDirectory(context);
        await File.WriteAllTextAsync(Path.Combine(context, "Dockerfile"), dockerfile, cancellationToken);

        // The files the Dockerfile COPYs in, beside it, by a plain name: nothing written outside the patch's folder.
        foreach (var file in request.Files ?? [])
        {
            if (file.Name.Trim().Length == 0 || Path.GetFileName(file.Name) != file.Name || file.Name is "Dockerfile" or "." or "..")
            {
                throw new ArgumentException($"{file.Name} is not a plain file name for the patch's folder.");
            }

            await File.WriteAllTextAsync(Path.Combine(context, file.Name), file.Text.ReplaceLineEndings("\n"), cancellationToken);
        }
        var moved = (request.Containers ?? []).Where(container => container.Trim().Length > 0).Distinct(StringComparer.Ordinal).ToList();

        // The image's card and its containers' say a patch is being built until the job ends, however it ends.
        marks.PatchBegins(image, patch, moved);
        events.Publish(new ChangeNotice([ChangeNotice.Container, ChangeNotice.Image]));
        var job = builds.Start(new BuildImageRequest(context, patch), (say, token) => MoveAsync(image, id, patch, moved, say, token), () =>
        {
            marks.PatchEnds(image);
            events.Publish(new ChangeNotice([ChangeNotice.Container, ChangeNotice.Image]));
        });
        marks.PatchJob(image, job.Job);
        return job;
    }

    /// <summary>The patch's name: the image's tag with <c>-patched</c>; a patch patched again keeps its own.</summary>
    internal static string Named(string image)
    {
        var (repository, tag) = ImageReference.Split(image);
        return ImageReference.IsPatched(image) ? $"{repository}:{tag}" : $"{repository}:{tag}{ImageReference.PatchedSuffix}";
    }

    /// <summary>
    /// The command that upgrades the image's system packages, by its
    /// distribution's package manager; empty when no system package has a fix
    /// published, or the image has no manager to install one with (a distroless
    /// image).
    /// </summary>
    internal static string SystemUpgrade(IReadOnlyCollection<Installed> packages, IEnumerable<ImageVulnerability> found)
    {
        if (!found.Any(v => v.Ecosystem != Installed.PyPI && v.FixedIn.Length > 0))
        {
            return "";
        }

        if (packages.Any(package => package.Ecosystem.StartsWith("Alpine:", StringComparison.Ordinal) && package.Name == "apk-tools"))
        {
            return "apk upgrade --no-cache";
        }

        return packages.Any(package => package.Name == "apt"
                && (package.Ecosystem.StartsWith("Debian:", StringComparison.Ordinal) || package.Ecosystem.StartsWith("Ubuntu:", StringComparison.Ordinal)))
            ? "apt-get update && DEBIAN_FRONTEND=noninteractive apt-get upgrade -y && rm -rf /var/lib/apt/lists/*"
            : "";
    }

    /// <summary>The command that would raise <paramref name="fixes"/>, by <see cref="Installer"/>. A suggestion the user reads before it runs.</summary>
    internal static string PythonInstall(IReadOnlyList<string> fixes)
    {
        if (fixes.Count == 0)
        {
            return "";
        }

        return $"{Installer} {string.Join(' ', fixes.Select(fix => $"\"{fix}\""))}";
    }

    internal const string Uv = "uv";

    /// <summary>
    /// The uv a patch brings an image that has none, from Astral's own image
    /// (<c>/uv</c> in it); checked on ghcr.io when written.
    /// </summary>
    internal const string UvImage = "ghcr.io/astral-sh/uv:0.13.6";

    /// <summary>
    /// The Python a patch installs into: the one the image runs as
    /// <c>python3</c>, a virtual environment's when its folder comes first in
    /// the PATH (an image made by uv: <c>/opt/hermes/.venv</c>, with no pip in
    /// it), else the system's.
    /// </summary>
    private const string PatchedPython = "--python \"$(command -v python3)\"";

    /// <summary>
    /// The installer, the names to follow: always uv, quicker than pip, never
    /// missing as pip is from an environment uv made, and it says what it
    /// changed; brought in by <see cref="UvSetup"/> when the image has none.
    /// </summary>
    internal const string Installer = $"uv pip install {PatchedPython} --no-cache -U";

    /// <summary>What takes a library out, by the same installer, the names to follow.</summary>
    internal const string Uninstaller = $"uv pip uninstall {PatchedPython}";

    /// <summary>The Dockerfile line that puts uv in an image that does not carry it, before <see cref="Installer"/> runs; empty when it does.</summary>
    internal static string UvSetup(bool carriesUv) =>
        carriesUv ? "" : $"COPY --from={UvImage} /uv /usr/local/bin/uv";

    /// <summary>The user's commands, one per line, blank ones left out.</summary>
    internal static List<string> Commands(string text) =>
        [.. (text ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>
    /// The patch's Dockerfile: the image; as root, the system's upgrade and then
    /// each of the user's commands (<c>RUN</c> added to a line that does not
    /// start with it); and its own user given back, since an image that runs as
    /// another would otherwise be refused the upgrade.
    /// </summary>
    internal static string Dockerfile(string image, string upgrade, IReadOnlyList<string> commands, string user)
    {
        var lines = new List<string> { $"FROM {image}", "USER root" };
        if (upgrade.Length > 0)
        {
            lines.Add($"RUN {upgrade}");
        }

        lines.AddRange(commands.Select(command => command.StartsWith("RUN ", StringComparison.OrdinalIgnoreCase) ? command : $"RUN {command}"));
        if (user.Length > 0 && user is not ("root" or "0" or "0:0"))
        {
            lines.Add($"USER {user}");
        }

        return string.Join('\n', lines) + "\n";
    }

    /// <summary>The image's id and the user it runs as (<c>Config.User</c>); empty for root, and both empty when it cannot be read.</summary>
    private async Task<(string Id, string User)> InspectAsync(string image, CancellationToken cancellationToken)
    {
        try
        {
            var result = await wslc.RunAsync(["image", "inspect", image], cancellationToken: cancellationToken);
            var inspected = JsonNode.Parse(result.Stdout) switch
            {
                JsonArray array => array.FirstOrDefault(),
                var single => single,
            };
            return (((string?)inspected?["Id"] ?? "").Trim(), ((string?)inspected?["Config"]?["User"] ?? "").Trim());
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException or JsonException)
        {
            return ("", "");
        }
    }

    /// <summary>
    /// Each container chosen, recreated on the patch with everything else it has
    /// and started if it was running; one that fails is said and the others go on,
    /// and the job ends in error naming it. A project's keeps the mark of the
    /// service it was made from, so its project's next save finds it as it left
    /// it, and is recorded on the patch (<see cref="PatchedServices"/>).
    /// </summary>
    private async Task MoveAsync(string image, string id, string patch, IReadOnlyList<string> moved, Action<string> say, CancellationToken cancellationToken)
    {
        var failed = new List<string>();
        foreach (var container in moved)
        {
            try
            {
                var details = await containers.DetailsAsync(container, cancellationToken);
                var (project, service) = ProjectLabels.Split(details.Form.Project);
                await containers.RecreateAsync(details.Id, details.Form with { Image = patch, Start = details.IsRunning, ProjectConfig = ConfigOf(details.Inspect) }, cancellationToken);
                if (project.Length == 0)
                {
                    say($"INFO: {details.Name} now runs {patch}.");
                    continue;
                }

                // A patch of a patch keeps the image the file names, and that image's id, as the first one recorded them.
                var before = patched.Of(project, service);
                patched.Record(new PatchedService(project, service, before?.Original ?? image, before?.OriginalId ?? id, patch, DateTimeOffset.Now));
                say($"INFO: {details.Name} now runs {patch}. Project {project} keeps it on the patch at every save, while its file names {before?.Original ?? image} and that image is the one patched.");
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

    /// <summary>The project's mark of the service a container was made from (<see cref="ProjectLabels.Config"/>), read from its inspect; empty for none.</summary>
    private static string ConfigOf(string inspect)
    {
        try
        {
            var inspected = JsonNode.Parse(inspect) switch
            {
                JsonArray array => array.FirstOrDefault(),
                var single => single,
            };
            return (string?)inspected?["Config"]?["Labels"]?[ProjectLabels.Config] ?? "";
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return "";
        }
    }
}
