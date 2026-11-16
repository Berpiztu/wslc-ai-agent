using System.Text.Json;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// Whether an image is whole (<see cref="ImageCheckScript"/>): asked by the
/// Images page for one image or all of them, by a pull as it ends, and before a
/// container is moved to an image, so a damaged one is said at once and in
/// words, not found out when a container made from it fails to start.
/// </summary>
public sealed class ImageIntegrity(IWslcRunner wslc, ILogger<ImageIntegrity> logger) : IImageIntegrity
{
    /// <summary>A store of two hundred layers is read in a quarter of a minute; a slow disk, more.</summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromMinutes(5);

    public async Task<ImageChecks> CheckAsync(string? image = null, CancellationToken cancellationToken = default)
    {
        var listed = await ListedAsync(cancellationToken);
        var asked = string.IsNullOrWhiteSpace(image) ? null : image.Trim();
        if (asked is not null)
        {
            listed = [.. listed.Where(row => Names(row, asked))];
            if (listed.Count == 0)
            {
                throw new KeyNotFoundException($"Image {asked} is not on this session.");
            }
        }

        // Only ids go on the command line, and only as the hexadecimal they are.
        List<string> ids = asked is null ? [] : [.. listed.Select(row => row.Id).Where(id => id.All(Uri.IsHexDigit)).Distinct(StringComparer.Ordinal)];
        var result = await wslc.RunAsync(["system", "session", "run", "sh", "-c", ImageCheckScript.Piped(ids)], CheckTimeout, cancellationToken);
        return Read(result.Stdout, listed)
            ?? throw new InvalidOperationException("The session's store is not laid out the way the agent knows, so its images cannot be checked.");
    }

    /// <summary>
    /// Why <paramref name="image"/> must not be used, when it is damaged; null
    /// when it is whole, is not on the machine yet, or could not be checked — a
    /// check that fails stops nothing: it is what is known to be damaged that is
    /// refused.
    /// </summary>
    public async Task<string?> WhyDamagedAsync(string image, CancellationToken cancellationToken = default)
    {
        try
        {
            return (await CheckAsync(image, cancellationToken)).Images.FirstOrDefault(check => check.IsDamaged)?.Why();
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
        catch (Exception failed) when (failed is WslcException or WslcNotFoundException or TimeoutException or InvalidOperationException or JsonException)
        {
            logger.LogWarning("image {Image} could not be checked: {Message}", image, failed.Message);
            return null;
        }
    }

    /// <summary>
    /// What the script reported, an image a line, named as the list names it;
    /// null when it said the store is not one it knows.
    /// </summary>
    internal static ImageChecks? Read(string output, IReadOnlyList<Listed> listed)
    {
        var lines = output.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(ImageCheckScript.Mark, StringComparison.Ordinal))
            .Select(line => line[ImageCheckScript.Mark.Length..].Split(' '))
            .ToList();
        if (lines.Any(fields => fields[0] == "LAYOUT") || lines.All(fields => fields[0] != "END"))
        {
            return null;
        }

        var damaged = lines
            .Where(fields => fields is ["BAD", _, _, _, _])
            .ToLookup(fields => fields[1], fields => new DamagedLayer(fields[2], Number(fields[3]), Number(fields[4])), StringComparer.Ordinal);
        var checks = new List<ImageCheck>();
        foreach (var fields in lines.Where(fields => fields is ["IMAGE", _, _, _]))
        {
            // One image may carry several names: each is said, since each is what someone would use.
            var names = listed.Where(row => fields[1].StartsWith(row.Id, StringComparison.Ordinal)).ToList();
            if (names.Count == 0)
            {
                names.Add(new Listed(fields[1][..Math.Min(12, fields[1].Length)], ""));
            }

            foreach (var row in names)
            {
                checks.Add(new ImageCheck(row.Reference.Length > 0 ? row.Reference : row.Id, row.Id, (int)Number(fields[2]), [.. damaged[fields[1]]]));
            }
        }

        return new ImageChecks([.. checks.OrderByDescending(check => check.IsDamaged).ThenBy(check => check.Image, StringComparer.Ordinal)]);
    }

    private static long Number(string text) => long.TryParse(text, out var value) ? value : 0;

    private async Task<IReadOnlyList<Listed>> ListedAsync(CancellationToken cancellationToken)
    {
        var result = await wslc.RunAsync(["image", "list", "--format", "json"], cancellationToken: cancellationToken);
        return [.. WslcJson.ParseRows(result.Stdout).Select(ToListed).Where(row => row.Id.Length > 0)];
    }

    /// <summary>A row of the image list: its id without the algorithm, and repository:tag when it has both.</summary>
    private static Listed ToListed(JsonElement row)
    {
        var id = row.GetString("ID") is { Length: > 0 } upper ? upper : row.GetString("Id");
        id = id.StartsWith("sha256:", StringComparison.Ordinal) ? id["sha256:".Length..] : id;
        var repository = row.GetString("Repository").Trim();
        var tag = row.GetString("Tag").Trim();
        var named = repository.Length > 0 && tag.Length > 0 && repository != "<none>" && tag != "<none>";
        return new Listed(id.Trim(), named ? $"{repository}:{tag}" : "");
    }

    /// <summary>The image asked for: by its reference, as typed or as wslc spells it, with <c>latest</c> when no tag was given, or by its id.</summary>
    private static bool Names(Listed row, string asked)
    {
        var spelled = ImageReference.Normalize(asked);
        var tagged = spelled[(spelled.LastIndexOf('/') + 1)..].Contains(':') ? spelled : $"{spelled}:latest";
        return string.Equals(row.Reference, asked, StringComparison.Ordinal)
            || string.Equals(row.Reference, tagged, StringComparison.Ordinal)
            || (asked.Length >= 12 && (row.Id.StartsWith(asked, StringComparison.Ordinal) || asked.StartsWith(row.Id, StringComparison.Ordinal)));
    }

    /// <summary>An image as the list names it.</summary>
    internal sealed record Listed(string Id, string Reference);
}