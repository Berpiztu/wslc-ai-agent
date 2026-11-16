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
/// Zaindari's patch: the image built again on itself, tagged <c>…-patched</c>
/// and, made for a container, <c>…-patched-CONTAINER</c>, its own and no
/// other's, with what its window asks. Its system packages upgraded (<c>apk upgrade</c>,
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

    /// <summary>
    /// The build started; null, nothing done, for a patch with nothing to do (no
    /// system upgrade asked or possible, no command).
    /// <para>
    /// What is patched is an image by its id, never by its name: the image each
    /// container ticked stands on, as its inspect says, or, with no container,
    /// the one the name given has now. A name can have moved to another image
    /// since the container was made (a newer pull took <c>latest</c>), and the
    /// patch is of what the container runs. <c>wslc</c> builds from a name
    /// alone, so that image is given one of the agent's own for the build
    /// (<see cref="BaseOf"/>), taken away again when the job ends.
    /// </para>
    /// </summary>
    public async Task<BuildJob?> StartIfAnyAsync(PatchImageRequest request, CancellationToken cancellationToken = default)
    {
        var image = ImageReference.Normalize(WslcArgs.Require(request.Image, "image"));
        foreach (var file in request.Files ?? [])
        {
            if (file.Name.Trim().Length == 0 || Path.GetFileName(file.Name) != file.Name || file.Name is "Dockerfile" or "." or "..")
            {
                throw new ArgumentException($"{file.Name} is not a plain file name for the patch's folder.");
            }
        }

        var (namedId, _) = await InspectAsync(image, cancellationToken);
        var moved = (request.Containers ?? []).Where(container => container.Trim().Length > 0).Distinct(StringComparer.Ordinal).ToList();

        // A container's patch is its own, named after it: what is upgraded, raised or accepted here was
        // chosen for the containers ticked, and no other container's patch is theirs to take or to lose
        // its name to. Each is an image of its own as well, with an id of its own, since it carries its
        // container's name in a label: two containers patched alike would otherwise be one image under
        // two names. The first is built by the job; the others after it, each from the image it stands
        // on, which the build's cache makes quick. With no container ticked it is the image's own patch.
        var owned = new List<OwnPatch>();
        foreach (var container in moved)
        {
            var details = await containers.DetailsAsync(container, cancellationToken);
            var stands = PinnedOf(details.Inspect) is { Length: > 0 } pinned ? pinned : namedId;
            var own = ImageReference.PatchedFor(IsId(details.Form.Image) ? image : details.Form.Image, details.Name);
            owned.Add(new OwnPatch(container, details.Name, own, stands, (await InspectAsync(own, cancellationToken)).Id));
        }

        // Read by its name while the name is that image's, so what is known of the name holds (a patch of
        // ours, an image built here); by its id once the name has moved to another image.
        var subjectId = owned.Count > 0 ? owned[0].Stands : namedId;
        var subject = subjectId.Length > 0 && subjectId != namedId ? subjectId : image;
        var report = await security.ReportAsync(subject, cancellationToken);
        var upgrade = request.System ? report.SystemUpgrade : "";
        var commands = Commands(request.Commands);
        var written = (request.Dockerfile ?? "").Trim();
        // A Dockerfile that adds no step to the image it starts from is the same image again: nothing to build.
        if (!report.Found || (written.Length == 0 ? upgrade.Length == 0 && commands.Count == 0 : !AddsStep(written)))
        {
            return null;
        }

        var (id, user) = await InspectAsync(subject, cancellationToken);
        var patch = owned.Count > 0 ? owned[0].Patch : IsId(image) ? $"{OwnRepository}:{Short(namedId)}" : Named(image);

        // Each image patched gets its build's name, and each patch its Dockerfile on it: the patch
        // window's as the user left it, its FROM put on that name when it names the image patched;
        // or written here from the choices. With the files it COPYs in beside it, kept under the
        // patch's own name, where its Changes read it; nothing is written outside a patch's folder.
        List<(string Patch, string Stands)> toBuild = owned.Count > 0 ? [.. owned.Select(own => (own.Patch, own.Stands))] : [(patch, subjectId)];
        var bases = new List<string>();
        foreach (var (named, stands) in toBuild)
        {
            var from = stands.Length > 0 ? BaseOf(stands) : image;
            if (stands.Length > 0 && !bases.Contains(from))
            {
                await wslc.RunAsync(["image", "tag", stands, from], cancellationToken: cancellationToken);
                bases.Add(from);
            }

            var folder = FolderOf(named);
            Directory.CreateDirectory(folder);
            var dockerfile = written.Length > 0
                ? Rebased(written.ReplaceLineEndings("\n") + "\n", from, [image, subject, stands, Short(stands)])
                : Dockerfile(from, upgrade, commands, user);
            await File.WriteAllTextAsync(Path.Combine(folder, "Dockerfile"), dockerfile, cancellationToken);
            foreach (var file in request.Files ?? [])
            {
                await File.WriteAllTextAsync(Path.Combine(folder, file.Name), file.Text.ReplaceLineEndings("\n"), cancellationToken);
            }
        }

        // The image's card and its containers' say a patch is being built until the job ends, however it ends.
        marks.PatchBegins(image, patch, moved);
        events.Publish(new ChangeNotice([ChangeNotice.Container, ChangeNotice.Image]));
        var job = builds.Start(new BuildImageRequest(FolderOf(patch), patch, Labels: owned.Count > 0 ? OwnerLabelOf(owned[0].Name) : ""), (say, token) => MoveAsync(image, id, patch, owned, say, token), () =>
        {
            marks.PatchEnds(image);
            events.Publish(new ChangeNotice([ChangeNotice.Container, ChangeNotice.Image]));
            _ = UnnameAsync(bases);
        });
        marks.PatchJob(image, job.Job);
        return job;
    }

    /// <summary>
    /// Zaindari's report of the image a container stands on: by the image's id
    /// when the name the container was made with has moved to another image,
    /// so what is read, and what its patch would start from, is what the
    /// container runs. Said under the name asked, with the container's own
    /// patch as the one it offers.
    /// </summary>
    public async Task<ImageVulnerabilities> ReportForAsync(string image, string container, CancellationToken cancellationToken = default)
    {
        var asked = ImageReference.Normalize(WslcArgs.Require(image, "image"));
        var details = await containers.DetailsAsync(WslcArgs.Require(container, "container"), cancellationToken);
        var (namedId, _) = await InspectAsync(asked, cancellationToken);
        var stands = PinnedOf(details.Inspect);
        var report = await security.ReportAsync(stands.Length > 0 && stands != namedId ? stands : asked, cancellationToken);
        return report with { Image = asked, Patch = report.Patch.Length > 0 ? ImageReference.PatchedFor(IsId(details.Form.Image) ? asked : details.Form.Image, details.Name) : "" };
    }

    /// <summary>The repository of the names the agent gives an image for a build: <c>wslc-agent-patch-base:3ea06e4cb95e</c>, its id's first twelve characters.</summary>
    internal const string BaseRepository = "wslc-agent-patch-base";

    /// <summary>The repository of the patch of an image that has no name of its own.</summary>
    internal const string OwnRepository = "wslc-agent-patched";

    /// <summary>The name an image is built from, made of its id: <c>wslc</c> takes no id after <c>FROM</c>.</summary>
    internal static string BaseOf(string imageId) => $"{BaseRepository}:{Short(imageId)}";

    /// <summary>An image id's first twelve characters, without its algorithm.</summary>
    internal static string Short(string imageId)
    {
        var bare = imageId.StartsWith("sha256:", StringComparison.Ordinal) ? imageId["sha256:".Length..] : imageId;
        return bare[..Math.Min(12, bare.Length)];
    }

    /// <summary>Whether a reference is an image's id and no name: with its algorithm, or twelve hexadecimal characters or more.</summary>
    internal static bool IsId(string reference) =>
        reference.StartsWith("sha256:", StringComparison.Ordinal) || (reference.Length >= 12 && reference.All(Uri.IsHexDigit));

    /// <summary>
    /// The Dockerfile with its first <c>FROM</c> put on <paramref name="from"/>,
    /// when it names the image patched in one of the ways it is
    /// <paramref name="known"/> by; a <c>FROM</c> the user pointed elsewhere is
    /// theirs, and stays.
    /// </summary>
    internal static string Rebased(string dockerfile, string from, IReadOnlyList<string> known)
    {
        var lines = dockerfile.Split('\n');
        for (var at = 0; at < lines.Length; at++)
        {
            var words = lines[at].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2 && words[0].Equals("FROM", StringComparison.OrdinalIgnoreCase))
            {
                if (known.Any(name => name.Length > 0 && string.Equals(name, words[1], StringComparison.Ordinal)))
                {
                    words[1] = from;
                    lines[at] = string.Join(' ', words);
                }

                break;
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>Whether a Dockerfile does anything to the image it starts from: a line that is not its FROM, the user it runs as, or a comment.</summary>
    internal static bool AddsStep(string dockerfile) =>
        dockerfile.ReplaceLineEndings("\n").Split('\n')
            .Select(line => line.TrimStart())
            .Any(line => line.Length > 0 && !line.StartsWith('#')
                && !line.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("USER ", StringComparison.OrdinalIgnoreCase));

    /// <summary>The image a container stands on, out of its inspect: its id with its algorithm; empty when it cannot be read.</summary>
    private static string PinnedOf(string inspect)
    {
        try
        {
            var inspected = JsonNode.Parse(inspect) switch
            {
                JsonArray array => array.FirstOrDefault(),
                var single => single,
            };
            return ((string?)inspected?["Image"] ?? "").Trim();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return "";
        }
    }

    /// <summary>The build's names taken away again. One that was its image's only name, with nothing on that image any more, takes the image with it: a patch's old base nobody uses.</summary>
    private async Task UnnameAsync(IReadOnlyList<string> bases)
    {
        foreach (var name in bases)
        {
            try
            {
                await wslc.RunAsync(["image", "remove", name]);
            }
            catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException)
            {
                // Still the only name of an image something stands on, or gone already: it stays, or there is nothing to take away.
            }
        }
    }
    /// <summary>The folder a patch is built in: its Dockerfile and the files beside it, kept after the build.</summary>
    private string FolderOf(string patch) =>
        Path.Combine(options.Value.DataDirectory, "image-patches", string.Join("_", patch.Split(Path.GetInvalidFileNameChars().Append(':').ToArray())));

    /// <summary>What the image's patch changed (<see cref="IImageSecurity.ChangesAsync"/>), with the Dockerfile it was last built by.</summary>
    public async Task<PatchChanges> ChangesAsync(string image, CancellationToken cancellationToken)
    {
        var changes = await security.ChangesAsync(image, cancellationToken);
        var dockerfile = Path.Combine(FolderOf(changes.Patched), "Dockerfile");
        return File.Exists(dockerfile) ? changes with { Dockerfile = await File.ReadAllTextAsync(dockerfile, cancellationToken) } : changes;
    }

    /// <summary>The label a container's patch carries: whose it is. It is also what makes it an image of its own.</summary>
    internal const string OwnerLabel = "ai.berpiztu.wslc.patch.container";

    private static string OwnerLabelOf(string container) => $"{OwnerLabel}={container}";

    /// <summary>How long the build of another container's patch may take: the first one's layers are in the cache.</summary>
    private static readonly TimeSpan OwnBuildTimeout = TimeSpan.FromMinutes(30);

    /// <summary>A container's own patch: the container as it was asked for and by its name, the patch's name, the id of the image the container stands on, which the patch is built from, and the id of the image that name had before this patch, empty when it had none.</summary>
    private sealed record OwnPatch(string Container, string Name, string Patch, string Stands, string PreviousId);

    /// <summary>The name of a patch made for no container: the image's tag with <c>-patched</c>; a patch patched again keeps its own. One made for a container is <see cref="ImageReference.PatchedFor"/>.</summary>
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

    /// <summary>
    /// The commands that would raise <paramref name="fixes"/>, by
    /// <see cref="Installer"/>, each library in the environment it is in
    /// (<see cref="PythonEnvironment.Of"/>), one command per environment. A
    /// suggestion the user reads before it runs.
    /// </summary>
    internal static string PythonInstall(IReadOnlyList<string> fixes, IReadOnlyList<PythonEnvironment> environments)
    {
        if (fixes.Count == 0)
        {
            return "";
        }

        return string.Join(" && ", fixes
            .GroupBy(fix => PythonEnvironment.Of(environments, [fix.Split(">=")[0]]) is { } environment ? $"{environment.Flags}{environment.Sources}" : "")
            .Select(environment => $"{Installer}{(environment.Key.Length > 0 ? $" {environment.Key}" : "")} {string.Join(' ', environment.Select(fix => $"\"{fix}\""))}"));
    }

    /// <summary>
    /// What Zaindari says of patching the image's Python libraries
    /// (<see cref="PatchCheck"/>), null when none is vulnerable. Refused when
    /// none of them is in an environment read from the image: where it is
    /// installed, and with which Python, is not known. Care, each said: several
    /// environments; builds for one variant; lines the application's own files
    /// pin with a reason; libraries no version fixes yet.
    /// </summary>
    internal static PatchCheck? Check(IReadOnlyList<PythonEnvironment> environments, IReadOnlyList<ImageTextFile> files, IReadOnlyList<ImageAdvisory> advisories)
    {
        var vulnerable = advisories.Where(advisory => advisory.Ecosystem == "PyPI" && advisory.Accepted is null).ToList();
        if (vulnerable.Count == 0)
        {
            return null;
        }

        var libraries = vulnerable.Select(advisory => advisory.Package).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (!libraries.Any(library => environments.Any(environment => environment.Holds(library))))
        {
            return new PatchCheck(PatchCheck.Refused,
                [$"{string.Join(", ", libraries)}: in no Python environment read from the image; where {(libraries.Count == 1 ? "it is" : "they are")} installed, and with which Python, is not known."]);
        }

        var care = new List<string>();
        if (environments.Count > 1)
        {
            care.Add($"Python in {environments.Count} environments, each library raised in the one it is in: "
                + string.Join("; ", environments.Select(environment => $"{environment.Python}{(environment.Venv ? "" : " (the system's)")}, {environment.Libraries.Count} libraries")) + ".");
        }

        foreach (var environment in environments)
        {
            foreach (var build in environment.Builds ?? [])
            {
                care.Add(build.Index.Length > 0
                    ? $"{build.Library} {build.Version} is a build for {build.Version.Split('+')[1]}: it comes from {build.Index}, and stays one."
                    : $"{build.Library} {build.Version} is a build for {build.Version.Split('+')[1]} from an index not known: it is left as it is.");
            }
        }

        foreach (var file in files.Where(file => PythonEnvironment.Of(environments, LibrariesOf(file.Text)) is not null))
        {
            foreach (var line in file.Text.ReplaceLineEndings("\n").Split('\n').Where(line => line.Contains("==", StringComparison.Ordinal) && line.Contains('#')))
            {
                care.Add($"{line.Split('#')[0].Trim()} is pinned in {file.Path}, its authors say why: {line[(line.IndexOf('#') + 1)..].Trim()}");
            }
        }

        if (vulnerable.Where(advisory => advisory.FixedIn.Length == 0).Select(advisory => advisory.Package).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList() is { Count: > 0 } unfixed)
        {
            care.Add($"No version fixes {string.Join(", ", unfixed)} yet: {(unfixed.Count == 1 ? "it stays" : "they stay")} vulnerable, or taken out.");
        }

        return new PatchCheck(care.Count > 0 ? PatchCheck.Care : PatchCheck.Clear, care);
    }

    /// <summary>The libraries a requirements file names, a line each: its name before any extras, version or condition.</summary>
    private static IEnumerable<string> LibrariesOf(string text) =>
        text.ReplaceLineEndings("\n").Split('\n')
            .Select(line => line.Split('#')[0].Trim())
            .Where(line => line.Length > 0 && char.IsLetterOrDigit(line[0]))
            .Select(line => string.Concat(line.TakeWhile(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-')));

    /// <summary>
    /// Where builds of <paramref name="library"/> for <paramref name="variant"/>
    /// come from (<c>torch 2.4.0+cpu</c>): PyTorch's own index for its
    /// libraries, one per variant (<c>https://download.pytorch.org/whl/cpu</c>);
    /// PyPI has only its default build, PyTorch's for CUDA, gigabytes of NVIDIA
    /// libraries with it. Empty for any other: where it came from is not known.
    /// </summary>
    internal static string IndexOf(string library, string variant) =>
        PyTorchLibraries.Contains(PythonEnvironment.Normalized(library)) && variant.Length > 0 ? $"{PyTorchIndex}/{variant}" : "";

    /// <summary>PyTorch's libraries, built for each variant on its own index.</summary>
    private static readonly HashSet<string> PyTorchLibraries = new(["torch", "torchvision", "torchaudio"], StringComparer.Ordinal);

    private const string PyTorchIndex = "https://download.pytorch.org/whl";

    internal const string Uv = "uv";

    /// <summary>
    /// The uv a patch brings an image that has none, from Astral's own image
    /// (<c>/uv</c> in it); checked on ghcr.io when written.
    /// </summary>
    internal const string UvImage = "ghcr.io/astral-sh/uv:0.13.6";

    /// <summary>
    /// The installer, the names to follow: always uv, quicker than pip, never
    /// missing as pip is from an environment uv made, and it says what it
    /// changed; brought in by <see cref="UvSetup"/> when the image has none.
    /// Never <c>-U</c>, which takes every library of a file and every one they
    /// need to its latest: what is installed and still meets the file stays as
    /// the image has it (agent-zero's <c>torch 2.4.0+cpu</c>, which
    /// <c>sentence-transformers</c> needs, would come back as PyPI's latest,
    /// built for CUDA, gigabytes of NVIDIA libraries with it). A library raised
    /// is installed because what is there no longer meets its line; one taken
    /// to its latest is named after <see cref="UpgradeOne"/>. Never built from
    /// its sources either (<c>--no-build</c>): only a version with a wheel for
    /// the image is taken, so a library its authors pin to the last one that
    /// has a wheel (agent-zero's <c>pyreqwest-impersonate==0.5.3</c>; 0.5.5 has
    /// its sources alone) is not compiled, and failed, in the build.
    /// Each command is given the environment it works in after it
    /// (<see cref="PythonEnvironment.Flags"/>), read from the image: an image may
    /// hold several, a virtual environment its application runs in, another of
    /// a tool's, the system's own.
    /// </summary>
    internal const string Installer = "uv pip install --no-cache --no-build";

    /// <summary>What takes one library, the name to follow, to its latest, and no other.</summary>
    internal const string UpgradeOne = "--upgrade-package";

    /// <summary>What takes a library out, by the same installer, its environment's flags and the names to follow.</summary>
    internal const string Uninstaller = "uv pip uninstall";

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
    /// Each container chosen, recreated on its own patch with everything else it
    /// has and started if it was running: the image just built, under the
    /// container's own name. One that fails is said and the others go on,
    /// and the job ends in error naming it. A project's keeps the mark of the
    /// service it was made from, so its project's next save finds it as it left
    /// it, and is recorded on the patch (<see cref="PatchedServices"/>).
    /// </summary>
    private async Task MoveAsync(string image, string id, string built, IReadOnlyList<OwnPatch> moved, Action<string> say, CancellationToken cancellationToken)
    {
        var failed = new List<string>();
        foreach (var own in moved)
        {
            var (container, patch) = (own.Container, own.Patch);
            try
            {
                if (patch != built)
                {
                    say($"INFO: Building {patch}, {own.Name}'s own, from the same Dockerfile.");
                    await wslc.RunAsync(ImageBuilds.Args(new BuildImageRequest(FolderOf(patch), patch, Labels: OwnerLabelOf(own.Name))), OwnBuildTimeout, cancellationToken);
                }

                var details = await containers.DetailsAsync(container, cancellationToken);
                var (project, service) = ProjectLabels.Split(details.Form.Project);
                await containers.RecreateAsync(details.Id, details.Form with { Image = patch, Start = details.IsRunning, ProjectConfig = ConfigOf(details.Inspect) }, cancellationToken);
                await ForgetPreviousAsync(own, say, cancellationToken);
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
            throw new InvalidOperationException($"{built} was built, but {string.Join(", ", failed)} {(failed.Count == 1 ? "was" : "were")} not moved to {(failed.Count == 1 ? "its" : "their")} patch.");
        }
    }

    /// <summary>
    /// A container patched again leaves its previous patch with no name and
    /// nothing on it: it was that container's alone, so it is removed, and no
    /// nameless image is left behind. One something still stands on is not
    /// removed by <c>wslc</c>, and stays.
    /// </summary>
    private async Task ForgetPreviousAsync(OwnPatch own, Action<string> say, CancellationToken cancellationToken)
    {
        if (own.PreviousId.Length == 0 || (await InspectAsync(own.Patch, cancellationToken)).Id == own.PreviousId)
        {
            return;
        }

        try
        {
            await wslc.RunAsync(["image", "remove", own.PreviousId], cancellationToken: cancellationToken);
            say($"INFO: {own.Name}'s previous patch was removed: it had no name left and nothing on it.");
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException)
        {
            say($"INFO: {own.Name}'s previous patch stays, without a name: {ex.Message}");
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
