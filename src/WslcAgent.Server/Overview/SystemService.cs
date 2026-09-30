using System.Text.Json;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Overview;

/// <summary>
/// The System page's snapshot and its cleanups.
/// Each part fails on its own: a CLI without <c>info</c>,
/// or an image list that errors, still leaves the rest of the page.
/// </summary>
public sealed class SystemService(
    IWslcRunner wslc,
    ISelectedSession selected,
    IImageService images,
    IVolumeService volumes,
    INetworkService networks) : ISystemService
{
    private const int TopImageCount = 8;

    private static readonly Dictionary<string, string> CleanupCommands = new(StringComparer.Ordinal)
    {
        ["images"] = "wslc image prune -f -a",
        ["volumes"] = "wslc volume prune -f -a",
        ["networks"] = "wslc network prune -f",
    };

    public async Task<SystemOverview> GetAsync(CancellationToken cancellationToken = default)
    {
        var version = await VersionAsync(cancellationToken);
        var (info, infoError) = await InfoAsync(cancellationToken);
        var (active, sessionsError) = await ActiveSessionsAsync(cancellationToken);

        var selectedName = selected.Name ?? "";
        var store = SessionStoreReader.Mark(SessionStoreReader.Read(), active, selectedName);
        var (primary, storagePath) = SystemParsing.CompactionTarget(store, active, selectedName);

        // Listing images opens the session the agent targets, and this page is
        // where a stopped one is compacted: with that one stopped, the images
        // are left unread and say so rather than starting the session the user
        // has just stopped. Another session running is no reason to read them —
        // they would not come from it.
        var (imageRows, imageError) = primary.Length > 0
            ? await ImagesAsync(cancellationToken)
            : ([], $"Session {selectedName} is not running, so images are not listed. Start it to see them.");

        return new SystemOverview(
            version,
            info,
            infoError,
            store,
            active,
            sessionsError,
            selectedName,
            primary,
            storagePath,
            // Only the session whose VHDX this is: it is the one holding the
            // file open. Any-session-running blocked a compaction because some
            // other store was in use.
            CompactionBlocked: primary.Length > 0,
            imageRows.Take(TopImageCount).ToList(),
            imageRows.Count,
            imageRows.Sum(image => image.SizeBytes),
            imageError);
    }

    public async Task<CleanupResult> CleanupAsync(string target, CancellationToken cancellationToken = default)
    {
        if (!CleanupCommands.TryGetValue(target, out var command))
        {
            throw new ArgumentException("Unknown cleanup target.", nameof(target));
        }

        var storeBefore = SessionStoreReader.Read().TotalBytes;
        var imagesBefore = target == "images" ? await ImageTotalAsync(cancellationToken) : null;

        var result = await PruneAsync(target, cancellationToken);

        var storeAfter = SessionStoreReader.Read().TotalBytes;
        var imagesAfter = target == "images" ? await ImageTotalAsync(cancellationToken) : null;

        var stdout = result.Stdout.Trim();
        var stderr = result.Stderr.Trim();
        var reclaimed = SystemParsing.ReclaimedBytes($"{stdout}\n{stderr}");
        var storeDelta = Math.Max(0, storeBefore - storeAfter);
        long? imageDelta = imagesBefore is { } before && imagesAfter is { } after ? Math.Max(0, before - after) : null;

        return new CleanupResult(
            target,
            command,
            stdout.Length > 0 ? stdout : stderr.Length > 0 ? stderr : $"Completed: {command}",
            SystemParsing.CleanupSummary(target, reclaimed, storeDelta, imageDelta),
            reclaimed,
            storeDelta,
            imageDelta);
    }

    /// <summary>The prune through the one service each resource already has; the page prunes every unused image, not only dangling ones.</summary>
    private Task<CommandOutput> PruneAsync(string target, CancellationToken cancellationToken) => target switch
    {
        "images" => images.PruneAsync(all: true, cancellationToken),
        "volumes" => volumes.PruneAsync(cancellationToken),
        _ => networks.PruneAsync(cancellationToken),
    };

    private async Task<string> VersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await wslc.RunAsync(["version"], cancellationToken: cancellationToken)).Stdout.Trim();
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException)
        {
            return ex.Message;
        }
    }

    /// <summary><c>wslc info</c>, then <c>wslc system info</c>: the first that says anything (older CLIs have neither).</summary>
    private async Task<(SystemRuntimeInfo Info, string Error)> InfoAsync(CancellationToken cancellationToken)
    {
        var error = "";
        foreach (var args in new[] { new[] { "info", "--format", "json" }, ["system", "info", "--format", "json"] })
        {
            try
            {
                var result = await wslc.RunAsync(args, cancellationToken: cancellationToken);
                var info = SystemParsing.RuntimeInfo(WslcJson.ParseRows(result.Stdout).FirstOrDefault());
                if (!info.IsEmpty)
                {
                    return (info, "");
                }
            }
            catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException or JsonException)
            {
                error = ex.Message;
            }
        }

        return (SystemRuntimeInfo.Empty, error);
    }

    private async Task<(IReadOnlyList<SystemImage> Rows, string Error)> ImagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await wslc.RunAsync(["image", "list", "--format", "json"], cancellationToken: cancellationToken);
            var rows = WslcJson.ParseRows(result.Stdout)
                .Select(ToImage)
                .OrderByDescending(image => image.SizeBytes)
                .ToList();
            return (rows, "");
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException or JsonException)
        {
            return ([], ex.Message);
        }
    }

    private async Task<long?> ImageTotalAsync(CancellationToken cancellationToken)
    {
        var (rows, error) = await ImagesAsync(cancellationToken);
        return error.Length > 0 ? null : rows.Sum(image => image.SizeBytes);
    }

    private async Task<(IReadOnlyList<ActiveSession> Sessions, string Error)> ActiveSessionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await wslc.RunAsync(["system", "session", "list"], cancellationToken: cancellationToken);
            return (SystemParsing.ActiveSessions(result.Stdout), "");
        }
        catch (Exception ex) when (ex is WslcException or WslcNotFoundException or TimeoutException)
        {
            return ([], ex.Message);
        }
    }

    /// <summary>A row of <c>wslc image list</c>: <c>Size</c> is bytes on older CLIs and text such as <c>8.67GB</c> on 2.9.8+.</summary>
    internal static SystemImage ToImage(JsonElement row)
    {
        var sizeText = row.GetString("Size");
        var size = long.TryParse(sizeText, out var plain) ? plain : StatsParsing.Bytes(sizeText);
        var created = row.GetString("Created");
        return new SystemImage(
            NoneAsEmpty(row.GetString("Repository")),
            NoneAsEmpty(row.GetString("Tag")),
            size,
            SystemParsing.Created(created.Length > 0 ? created : row.GetString("CreatedAt")));
    }

    private static string NoneAsEmpty(string value) => value.Trim() == "<none>" ? "" : value.Trim();
}
