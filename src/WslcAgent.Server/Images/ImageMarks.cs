using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// What Zaindari last found in each image, kept on the image, by its id: the
/// worst severity of its vulnerabilities moderate or worse, whether the agent
/// refuses it (built here, Block), when it was checked, and the names it was
/// checked by. Zaindari checks images, so what it finds belongs to the image:
/// the Images page paints the image by it, and a container reads its image's
/// mark by its image's name, so a container and its image never say two
/// things. Kept in <c>image-marks.json</c>, across restarts of the agent; set
/// at every check of the image (a build, a new container, its window, an
/// image a container was made from around the agent) and let go when a check
/// finds it clean. A name checked again under a new id (built again, pulled
/// anew) moves to the new image's mark.
/// </summary>
public sealed class ImageMarks(IOptions<WslcOptions> options, ILogger<ImageMarks> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path = Path.Combine(options.Value.DataDirectory, "image-marks.json");
    private readonly Lock _gate = new();
    private ConcurrentDictionary<string, ImageMark>? _marks;

    /// <summary>
    /// What a check of the image just found, under its id and the name it was
    /// checked by: a mark with its worst severity, or none when
    /// <paramref name="worst"/> is empty.
    /// </summary>
    public void Record(string id, string reference, string worst, bool refused)
    {
        id = Bare(id);
        if (id.Length == 0)
        {
            return;
        }

        var marks = Marks();
        // The name is this image's now: an older image it named lets it go.
        foreach (var (otherId, other) in marks.Where(kept => kept.Key != id && kept.Value.References.Any(name => SameReference(name, reference))).ToList())
        {
            var names = other.References.Where(name => !SameReference(name, reference)).ToList();
            if (names.Count == 0)
            {
                marks.TryRemove(otherId, out _);
            }
            else
            {
                marks[otherId] = other with { References = names };
            }
        }

        if (worst.Length == 0)
        {
            marks.TryRemove(id, out _);
        }
        else
        {
            var names = marks.TryGetValue(id, out var kept) ? kept.References : [];
            marks[id] = new ImageMark(worst, refused, DateTimeOffset.Now,
                names.Any(name => SameReference(name, reference)) ? names : [.. names, reference.Trim()]);
        }

        Save();
    }

    /// <summary>The mark of the image a name stands for; null for an image with none.</summary>
    public ImageMark? Of(string reference) =>
        reference.Trim().Length == 0 ? null : Marks().Values.FirstOrDefault(mark => mark.References.Any(name => SameReference(name, reference)));

    /// <summary>The images list's rows with what was found in each, matched by id (a short id and a full one alike).</summary>
    public IReadOnlyList<ImageSummary> Annotate(IReadOnlyList<ImageSummary> images)
    {
        var marks = Marks();
        return marks.IsEmpty
            ? images
            : [.. images.Select(image => marks.FirstOrDefault(kept => SameId(kept.Key, image.Id)) is { Value: { } mark }
                ? image with { ImageWorst = mark.Worst, ImageRefused = mark.Refused, ImageCheckedAt = Stamp(mark) }
                : image)];
    }

    /// <summary>The containers list's rows with their image's mark, read by the name of their image.</summary>
    public IReadOnlyList<ContainerSummary> AnnotateContainers(IReadOnlyList<ContainerSummary> containers) =>
        Marks().IsEmpty
            ? containers
            : [.. containers.Select(row => Of(row.Image) is { } mark
                ? row with { ImageWorst = mark.Worst, ImageRefused = mark.Refused, ImageCheckedAt = Stamp(mark) }
                : row)];

    /// <summary>The same image, as a list row names it and as a menu or a build asks for it: a reference with no tag is its <c>latest</c>.</summary>
    public static bool SameReference(string a, string b)
    {
        static string Tagged(string image)
        {
            image = image.Trim();
            var name = image[(image.LastIndexOf('/') + 1)..];
            return name.Contains(':') || name.Contains('@') ? image : image + ":latest";
        }

        return a.Trim().Length > 0 && string.Equals(Tagged(a), Tagged(b), StringComparison.OrdinalIgnoreCase);
    }

    private static string Stamp(ImageMark mark) => mark.At.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    private static string Bare(string id) => id.Trim().Replace("sha256:", "", StringComparison.OrdinalIgnoreCase);

    private static bool SameId(string kept, string id) =>
        Bare(id) is { Length: > 0 } bare && (kept.StartsWith(bare, StringComparison.OrdinalIgnoreCase) || bare.StartsWith(kept, StringComparison.OrdinalIgnoreCase));

    private ConcurrentDictionary<string, ImageMark> Marks()
    {
        lock (_gate)
        {
            if (_marks is null)
            {
                try
                {
                    _marks = File.Exists(_path)
                        ? new ConcurrentDictionary<string, ImageMark>(JsonSerializer.Deserialize<Dictionary<string, ImageMark>>(File.ReadAllText(_path), Json) ?? [])
                        : new ConcurrentDictionary<string, ImageMark>();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    logger.LogWarning("image marks could not be read from {Path}: {Message}", _path, ex.Message);
                    _marks = new ConcurrentDictionary<string, ImageMark>();
                }
            }

            return _marks;
        }
    }

    private void Save()
    {
        var marks = Marks();
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(marks, Json));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("image marks could not be written to {Path}: {Message}", _path, ex.Message);
            }
        }
    }
}

/// <summary>What Zaindari last found in one image: its worst severity, whether it is refused, when, and the names it was checked by.</summary>
public sealed record ImageMark(string Worst, bool Refused, DateTimeOffset At, IReadOnlyList<string> References);
