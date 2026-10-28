using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// A patch's Python lines tried before it is built (<c>POST
/// /api/v1/image-security/patch/dry-run</c>): each install run with
/// <c>--dry-run</c> in a container of the image that only sleeps, the Files
/// view's kind (<see cref="FilesHelpers.OpenImageAsync"/>), removed after. It
/// says what each would change, library by library, and what calls for care: a
/// build for one variant that would become another, NVIDIA's libraries it
/// would bring, libraries the image does not have; or why it would fail. Seconds,
/// where the build is minutes, and nothing is written. An image without uv has
/// it copied in, uv's own binary, taken once from Astral's image and kept
/// (<see cref="ImagePatches.UvImage"/>). Only the patch's own commands are
/// run, read word by word, never a shell.
/// </summary>
public sealed class PatchDryRuns(IWslcRunner wslc, FilesHelpers helpers, IOptions<WslcOptions> options, ILogger<PatchDryRuns> logger)
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromMinutes(10);

    /// <summary>What a command may hold besides its words and its quoted values: nothing a shell would read.</summary>
    private static readonly char[] Unsafe = [';', '&', '|', '$', '`', '<', '>', '(', ')', '{', '}', '\'', '\\', '\n', '\r'];

    /// <summary>Where uv goes in a container of an image that has none.</summary>
    private const string CopiedUv = "/tmp/uv";

    private readonly string _tools = Path.Combine(options.Value.DataDirectory, "tools");

    public async Task<PatchDryRun> RunAsync(PatchDryRunRequest request, CancellationToken cancellationToken)
    {
        var image = WslcArgs.Require(request.Image, "image");
        var commands = (request.Commands ?? []).Select(command => (Command: command.Trim(), Words: Words(command))).ToList();
        if (commands.Count == 0)
        {
            return new PatchDryRun([]);
        }

        var session = await helpers.OpenImageAsync(image, cancellationToken);
        try
        {
            var uv = await UvInAsync(session.Container, cancellationToken);
            var steps = new List<PatchDryRunStep>();
            foreach (var (command, words) in commands)
            {
                steps.Add(words[1] == "uninstall"
                    ? new PatchDryRunStep(command, [.. words.Skip(2).Where(word => !word.StartsWith('-') && !word.StartsWith('/')).Select(library => $"out: {library}")], [], "", [])
                    : await TryAsync(session.Container, uv, command, words, cancellationToken));
            }

            return new PatchDryRun(steps);
        }
        finally
        {
            await helpers.RemoveHelperAsync(session.Container);
        }
    }

    /// <summary>
    /// A patch command's words after <c>uv</c>: <c>pip install</c> or <c>pip
    /// uninstall</c> and their arguments, a quoted value one word without its
    /// quotes. Anything else, or anything a shell would read, is refused.
    /// </summary>
    internal static List<string> Words(string command)
    {
        var text = (command ?? "").Trim();
        if (text.IndexOfAny(Unsafe) >= 0 || !(text.StartsWith("uv pip install ", StringComparison.Ordinal) || text.StartsWith("uv pip uninstall ", StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Only the patch's own uv pip install and uninstall lines are tried: {text}");
        }

        var words = new List<string>();
        var word = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in text["uv ".Length..])
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ' ' && !quoted)
            {
                if (word.Length > 0)
                {
                    words.Add(word.ToString());
                    word.Clear();
                }
            }
            else
            {
                word.Append(c);
            }
        }

        if (quoted)
        {
            throw new ArgumentException($"A quote is not closed: {text}");
        }

        if (word.Length > 0)
        {
            words.Add(word.ToString());
        }

        return words;
    }

    /// <summary>One install tried: what uv says it would change, or why it would fail.</summary>
    private async Task<PatchDryRunStep> TryAsync(string container, string uv, string command, List<string> words, CancellationToken cancellationToken)
    {
        string said;
        var error = "";
        try
        {
            var result = await wslc.RunAsync(["container", "exec", container, uv, words[0], words[1], "--dry-run", .. words.Skip(2)], StepTimeout, cancellationToken);
            said = $"{result.Stdout}\n{result.Stderr}";
        }
        catch (WslcException ex)
        {
            said = $"{ex.Result.Stdout}\n{ex.Result.Stderr}";
            error = Failure(said) is { Length: > 0 } why ? why : ex.Message;
        }

        var output = said.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var (changes, warnings) = Changes(output);
        return new PatchDryRunStep(command, changes, warnings, error, output);
    }

    /// <summary>
    /// What uv's dry run says it would change (<c> - torch==2.4.0+cpu</c>,
    /// <c> + torch==2.14.1+cpu</c>), a library a line: raised, new, taken out;
    /// and what calls for care: a build for one variant that would become
    /// another, NVIDIA's libraries brought in.
    /// </summary>
    internal static (List<string> Changes, List<string> Warnings) Changes(IReadOnlyList<string> output)
    {
        var gone = Listed(output, "- ");
        var come = Listed(output, "+ ");
        var changes = new List<string>();
        var warnings = new List<string>();
        foreach (var (library, version) in come)
        {
            if (gone.TryGetValue(library, out var was))
            {
                changes.Add($"{library} {was} → {version}");
                if (was.Contains('+') && was.Split('+')[1] != (version.Contains('+') ? version.Split('+')[1] : ""))
                {
                    warnings.Add($"{library} {was} is a build for {was.Split('+')[1]}, and would become {version}.");
                }
            }
            else
            {
                changes.Add($"new: {library} {version}");
            }
        }

        changes.AddRange(gone.Where(library => !come.ContainsKey(library.Key)).Select(library => $"out: {library.Key} {library.Value}"));
        if (come.Keys.Where(library => library.StartsWith("nvidia-", StringComparison.OrdinalIgnoreCase)).ToList() is { Count: > 0 } nvidia)
        {
            warnings.Add($"It would bring {nvidia.Count} of NVIDIA's libraries (CUDA), gigabytes: {string.Join(", ", nvidia)}.");
        }

        if (come.Keys.Count(library => !gone.ContainsKey(library)) is var added and > 0)
        {
            warnings.Add($"It would add {added} {(added == 1 ? "library" : "libraries")} the image does not have.");
        }

        return (changes, warnings);
    }

    private static Dictionary<string, string> Listed(IReadOnlyList<string> output, string mark) =>
        output.Where(line => line.StartsWith(mark, StringComparison.Ordinal) && line.Contains("==", StringComparison.Ordinal))
            .Select(line => line[mark.Length..].Split("==", 2))
            .GroupBy(pair => pair[0].Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Key, pair => pair.First()[1].Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Why uv would fail, as it says it: its <c>×</c> and <c>╰─▶</c> lines, and the hint; empty when it said none.</summary>
    private static string Failure(string said) =>
        string.Join(" ", said.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith('×') || line.StartsWith("╰─▶", StringComparison.Ordinal) || line.StartsWith("hint:", StringComparison.Ordinal) || line.StartsWith("error:", StringComparison.Ordinal))
            .Take(6));

    /// <summary>The uv to run in the container: its own, else one copied in.</summary>
    private async Task<string> UvInAsync(string container, CancellationToken cancellationToken)
    {
        try
        {
            await wslc.RunAsync(["container", "exec", container, ImagePatches.Uv, "--version"], cancellationToken: cancellationToken);
            return ImagePatches.Uv;
        }
        catch (WslcException)
        {
            // None where its system looks for commands: Astral's own is copied in.
        }

        await wslc.RunAsync(["container", "cp", await HostUvAsync(cancellationToken), $"{container}:{Path.GetDirectoryName(CopiedUv)!.Replace('\\', '/')}"], cancellationToken: cancellationToken);
        await wslc.RunAsync(["container", "exec", container, "chmod", "+x", CopiedUv], cancellationToken: cancellationToken);
        return CopiedUv;
    }

    /// <summary>uv's binary on this machine, taken once from Astral's image (a container made of it, never started) and kept by its version.</summary>
    private async Task<string> HostUvAsync(CancellationToken cancellationToken)
    {
        var file = Path.Combine(_tools, $"uv-{ImagePatches.UvImage.Split(':')[^1]}", ImagePatches.Uv);
        if (File.Exists(file))
        {
            return file;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        // Named as the Files view's helpers are, so no list shows it the second it lives.
        var holder = $"{FilesHelpers.ImagePrefix}uv-{Guid.NewGuid():N}"[..(FilesHelpers.ImagePrefix.Length + 11)];
        try
        {
            await wslc.RunAsync(["container", "create", "--name", holder, ImagePatches.UvImage], StepTimeout, cancellationToken);
            await wslc.RunAsync(["container", "cp", $"{holder}:/{ImagePatches.Uv}", Path.GetDirectoryName(file)!], cancellationToken: cancellationToken);
            logger.LogInformation("uv taken from {Image} into {File}, for patches tried in images that have none", ImagePatches.UvImage, file);
            return file;
        }
        finally
        {
            await helpers.RemoveHelperAsync(holder);
        }
    }
}
