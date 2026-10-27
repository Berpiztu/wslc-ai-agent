using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// The version an image says it is, where it says it: its
/// <c>org.opencontainers.image.version</c> label, which an image's maker
/// writes; or else the one <c>*_VERSION</c> variable it sets, as the official
/// images do (<c>REDIS_VERSION=7.4.11</c>), with that variable's name, since
/// it may be the image it was built on's (<c>PYTHON_VERSION</c> in an
/// application built on python). None when the image says neither, or sets
/// several such variables and so does not say which. An image is read once,
/// by its id: what an id holds never changes. The images and the containers
/// lists carry it (<see cref="ImageSummary.ImageVersion"/>,
/// <see cref="ContainerSummary.ImageVersion"/>).
/// </summary>
public sealed partial class ImageVersions(IWslcRunner wslc)
{
    private readonly ConcurrentDictionary<string, ImageVersion> _byId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The images' rows with the version each says it is.</summary>
    public async Task<IReadOnlyList<ImageSummary>> AnnotateAsync(IReadOnlyList<ImageSummary> images, CancellationToken cancellationToken)
    {
        await ReadAsync(images.Select(image => image.Id), cancellationToken);
        return [.. images.Select(image => _byId.TryGetValue(image.Id, out var version) && version.Version.Length > 0
            ? image with { ImageVersion = version.Version, ImageVersionFrom = version.From }
            : image)];
    }

    /// <summary>
    /// The containers' rows, each with its image's version where its labels said none (a label it carries is
    /// its image's, and already read): the image found by the name the container was made from.
    /// </summary>
    public async Task<IReadOnlyList<ContainerSummary>> AnnotateAsync(IReadOnlyList<ContainerSummary> containers, CancellationToken cancellationToken)
    {
        if (!containers.Any(row => row.ImageVersion.Length == 0 && row.Image.Length > 0))
        {
            return containers;
        }

        var ids = await IdsAsync(cancellationToken);
        string? IdOf(string image) =>
            ids.FirstOrDefault(named => ImageMarks.SameReference(named.Key, image)).Value;

        await ReadAsync(containers.Where(row => row.ImageVersion.Length == 0).Select(row => IdOf(row.Image) ?? ""), cancellationToken);
        return [.. containers.Select(row => row.ImageVersion.Length == 0 && IdOf(row.Image) is { } id && _byId.TryGetValue(id, out var version) && version.Version.Length > 0
            ? row with { ImageVersion = version.Version, ImageVersionFrom = version.From }
            : row)];
    }

    /// <summary>Each image not read yet, read now; one that cannot be read is asked again the next time.</summary>
    private async Task ReadAsync(IEnumerable<string> ids, CancellationToken cancellationToken)
    {
        foreach (var id in ids.Where(id => id.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Where(id => !_byId.ContainsKey(id)).ToList())
        {
            try
            {
                var result = await wslc.RunAsync(["image", "inspect", id, "--format", "json"], cancellationToken: cancellationToken);
                if (WslcJson.ParseRows(result.Stdout).FirstOrDefault() is { ValueKind: JsonValueKind.Object } inspected)
                {
                    _byId[id] = Of(inspected);
                }
            }
            catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException or JsonException)
            {
                // Not there any more, or wslc not answering: nothing is said of it until it can be read.
            }
        }
    }

    /// <summary>
    /// The images by their names (<c>repository:tag</c>) and their short ids, as the list says now: a pull or a
    /// recreate moves a name to another id, and the runner forgets the list it keeps a few seconds when they run.
    /// </summary>
    private async Task<Dictionary<string, string>> IdsAsync(CancellationToken cancellationToken)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var result = await wslc.RunAsync(["image", "list", "--format", "json"], cancellationToken: cancellationToken);
            foreach (var row in WslcJson.ParseRows(result.Stdout))
            {
                var (repository, tag, id) = (row.GetString("Repository"), row.GetString("Tag"), row.GetString("ID"));
                if (repository is { Length: > 0 } and not "<none>" && tag is { Length: > 0 } and not "<none>" && id.Length > 0)
                {
                    ids[$"{repository}:{tag}"] = id.Replace("sha256:", "", StringComparison.OrdinalIgnoreCase) is { Length: > 12 } full ? full[..12] : id;
                }
            }
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException or JsonException)
        {
            // No names today: what the containers' images say is said at the next reading.
        }

        return ids;
    }

    /// <summary>
    /// What an image says of its version: its label, or else its one <c>*_VERSION</c> variable, named. From
    /// <c>image inspect</c> (its <c>Config</c>) or from the configuration a registry keeps for it (its <c>config</c>).
    /// </summary>
    internal static ImageVersion Of(JsonElement inspected)
    {
        var config = (inspected.TryGetProperty("Config", out var c) || inspected.TryGetProperty("config", out c)) && c.ValueKind == JsonValueKind.Object ? c : default;
        if (config.ValueKind == JsonValueKind.Object
            && config.TryGetProperty("Labels", out var labels) && labels.ValueKind == JsonValueKind.Object
            && labels.TryGetProperty(VersionLabel, out var label) && label.ValueKind == JsonValueKind.String
            && label.GetString()?.Trim() is { Length: > 0 } version)
        {
            return new ImageVersion(version, "");
        }

        var variables = config.ValueKind == JsonValueKind.Object && config.TryGetProperty("Env", out var env) && env.ValueKind == JsonValueKind.Array
            ? env.EnumerateArray().Select(entry => VersionVariable().Match(entry.GetString() ?? "")).Where(match => match.Success).ToList()
            : [];
        return variables is [var only]
            ? new ImageVersion(only.Groups["value"].Value.Trim(), only.Groups["name"].Value)
            : ImageVersion.None;
    }

    private const string VersionLabel = "org.opencontainers.image.version";

    [GeneratedRegex(@"^(?<name>[A-Z][A-Z0-9_]*_VERSION)=(?<value>\S.*)$")]
    private static partial Regex VersionVariable();
}

/// <summary>The version an image says it is, and the variable that says it; empty <see cref="From"/> for its label.</summary>
public sealed record ImageVersion(string Version, string From)
{
    public static readonly ImageVersion None = new("", "");
}
