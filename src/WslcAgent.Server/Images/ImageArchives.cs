using System.Text.RegularExpressions;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// Import and Load from an archive the browser uploads: the
/// upload is written to a temporary file with the same extensions, handed to
/// <c>wslc import FILE [IMAGE]</c> or <c>wslc load --input FILE</c>, and deleted
/// whatever the outcome. Both can take minutes for a large image.
/// </summary>
public sealed partial class ImageArchives(IWslcRunner wslc)
{
    private static readonly TimeSpan ArchiveTimeout = TimeSpan.FromHours(1);

    public async Task ImportAsync(Stream archive, string fileName, string image, CancellationToken cancellationToken = default)
    {
        var name = image.Trim();
        await WithTemporaryCopyAsync(archive, fileName, cancellationToken, path =>
        {
            var args = new List<string> { "import", path };
            if (name.Length > 0)
            {
                args.Add(WslcArgs.Require(name, "image name"));
            }

            return wslc.RunAsync(args, ArchiveTimeout, cancellationToken);
        });
    }

    public async Task<LoadImagesResult> LoadAsync(Stream archive, string fileName, CancellationToken cancellationToken = default)
    {
        WslcResult? result = null;
        await WithTemporaryCopyAsync(archive, fileName, cancellationToken, async path =>
            result = await wslc.RunAsync(["load", "--input", path], ArchiveTimeout, cancellationToken));
        return new LoadImagesResult(Loaded(result!.Stdout, result.Stderr), result.Stdout, result.Stderr);
    }

    /// <summary>What <c>wslc load</c> reports loading (2.9.4+): the values after <c>Loaded image:</c> or <c>Loaded image ID:</c>, once each.</summary>
    internal static IReadOnlyList<string> Loaded(string stdout, string stderr) =>
        LoadedLine().Matches(string.Join('\n', new[] { stdout, stderr }.Where(part => part.Length > 0)))
            .Select(match => match.Groups[1].Value.Trim().Trim('\'', '"'))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static async Task WithTemporaryCopyAsync(Stream archive, string fileName, CancellationToken cancellationToken, Func<string, Task> use)
    {
        var path = Path.Combine(Path.GetTempPath(), $"wslc-agent-archive-{Guid.NewGuid():N}{Extensions(fileName)}");
        try
        {
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
            {
                await archive.CopyToAsync(file, cancellationToken);
            }

            await use(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary><c>image.tar.gz</c> keeps <c>.tar.gz</c>: the CLI reads the compression from it.</summary>
    private static string Extensions(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var dot = name.IndexOf('.');
        return dot > 0 ? name[dot..] : ".tar";
    }

    [GeneratedRegex(@"loaded image(?: id)?:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex LoadedLine();
}
