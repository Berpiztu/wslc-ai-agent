using WslcAgent.ApiClient;
using WslcAgent.Server.Notifications;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// Checks of one image the agent owns (<see cref="ImageJobs"/>), as it owns a
/// pull: asked for from the image's card, the window that asked can close, the
/// card shows how far it has got, opens what it found and stops it. The work is
/// <see cref="ImageCheckScript"/>, run in the session; a check that finds the
/// image damaged ends as a failed job, with why, and stays on the card until
/// it is dismissed.
/// </summary>
public sealed class ImageCheckJobs(IWslcRunner wslc, ICliActivity activity, Notifier notifier, ILogger<ImageCheckJobs> logger)
    : ImageJobs(wslc, activity, notifier, logger)
{
    protected override string Verb => "Check";

    protected override string DoneStatus => "Whole";

    protected override IReadOnlyList<string> Command { get; } = ["system", "session", "run", "sh", "-c", ImageCheckScript.ForArgument];

    /// <summary>How far the check has got: the last layer measured, of how many.</summary>
    protected override (int Pct, string Status) Read(string output)
    {
        var layer = Said(output).LastOrDefault(fields => fields is ["LAYER", _, _, _, _]);
        return layer is not null && int.TryParse(layer[2], out var done) && int.TryParse(layer[3], out var total) && total > 0
            ? (Math.Clamp(done * 100 / total, 0, 100), $"Layer {done} of {total}")
            : (0, "Reading the image's layers…");
    }

    /// <summary>A check fails when its command did, when the image is not there, and when the image is damaged.</summary>
    protected override string? Failure(string image, string output, int exitCode)
    {
        if (base.Failure(image, output, exitCode) is { } failed)
        {
            return failed;
        }

        var log = PullProgress.Log(output).Log;
        if (Said(output).Any(fields => fields[0] == "MISSING"))
        {
            return $"Image {image} is not on this session.";
        }

        if (ImageIntegrity.Read(log, []) is not { Images.Count: > 0 } checks)
        {
            return $"Image {image} could not be checked: the session's store is not laid out the way the agent knows.";
        }

        return checks.Images.FirstOrDefault(check => check.IsDamaged) is { } damaged ? (damaged with { Image = image }).Why() : null;
    }

    /// <summary>The script writes for the agent; the console says the same in words.</summary>
    protected override string Shown(string log) =>
        string.Join('\n', log.Split('\n').Select(Worded).Where(line => line is not null));

    private static string? Worded(string line)
    {
        var text = line.Trim();
        if (!text.StartsWith(ImageCheckScript.Mark, StringComparison.Ordinal))
        {
            return line;
        }

        return text[ImageCheckScript.Mark.Length..].Split(' ') switch
        {
            ["LAYER", _, var done, var total, var state] => $"Layer {done} of {total}: {(state == "ok" ? "whole" : "DAMAGED")}",
            ["BAD", _, var folder, var want, var have] => $"    its folder {folder} should hold {Size(want)} and holds {Size(have)}",
            ["IMAGE", _, var count, "0"] => $"Whole: its {count} layers hold what was recorded for them.",
            ["IMAGE", _, var count, var damaged] => $"Damaged: {damaged} of its {count} layers lost file contents. Remove the image and pull it again.",
            ["MISSING", .. var asked] => $"Image {string.Join(' ', asked)} is not on this session.",
            ["LAYOUT", ..] => "The session's store is not laid out the way the agent knows.",
            _ => null,
        };
    }

    private static string Size(string bytes) => long.TryParse(bytes, out var value) ? Bytes.Humanize(value) : bytes;

    /// <summary>The lines the script marked, in order, each as its words.</summary>
    private static IEnumerable<string[]> Said(string output) =>
        PullProgress.Log(output).Log.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(ImageCheckScript.Mark, StringComparison.Ordinal))
            .Select(line => line[ImageCheckScript.Mark.Length..].Split(' '));
}