using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// Zaindari's report of one image as the lines of a log, which the logs view
/// colours by their level: the same in the window of a container or an image
/// (<c>ImageVulnerabilitiesDialog</c>) and in a project's
/// (<c>ProjectVulnerabilitiesDialog</c>).
/// </summary>
public static class ImageReportLines
{
    /// <summary>
    /// One image's lines, the least first: the view stands at the end, as a
    /// log's does, so what is read without scrolling is the worst. Its
    /// vulnerabilities from the low to the critical, then what fixes each
    /// library, and last the image in a line and, when the agent makes no
    /// container from it, that it does not. Its libraries themselves are not
    /// listed: only what is wrong with them.
    /// </summary>
    public static IEnumerable<string> Of(ImageVulnerabilities report)
    {
        if (!report.Found || !report.HasPython)
        {
            yield return Line("INFO", report.Note);
            yield break;
        }

        foreach (var v in report.Vulnerabilities.Reverse())
        {
            // Accepted, in grey: said with who, when and why, and counted nowhere.
            yield return Line(v.Accepted is null ? ImageRemedies.LevelOf(v.Severity) : "INFO",
                $"{Named(v.Package, v.Ecosystem)} {v.Version}, {v.Severity}: {v.Advisory}{(v.Cve.Length > 0 ? $" ({v.Cve})" : "")}, "
                + $"{ImageRemedies.FixSaid(v.FixedIn, v.UbuntuProFixedIn)}"
                + (v.Accepted is { } accepted ? $"; accepted by {accepted.By} on {accepted.At.ToLocalTime():yyyy-MM-dd}: {accepted.Reason}" : "")
                + $"  {v.Url}");
        }

        foreach (var remedy in report.Remedies)
        {
            yield return Line(ImageRemedies.LevelOfLine(remedy), remedy);
        }

        if (report.Note.Length > 0)
        {
            yield return Line("WARNING", report.Note);
        }

        var counted = string.Join(", ", new[] { "critical", "high", "moderate", "low", "unknown" }
            .Select(severity => (severity, count: report.Vulnerabilities.Count(v => v.Severity == severity)))
            .Where(pair => pair.count > 0)
            .Select(pair => $"{pair.count} {pair.severity}"));
        var raised = report.Vulnerabilities.Where(v => v.Accepted is null).ToList();
        var acceptedCount = report.Vulnerabilities.Count - raised.Count;
        var worst = raised.Count == 0 ? "INFO" : ImageRemedies.LevelOf(raised[0].Severity);
        yield return Line(worst,
            $"{report.Image}, {(report.Built ? "built on this machine" : "from a registry")}: {Counted(report.Packages)}, "
            + (report.Vulnerabilities.Count == 0 ? "no known vulnerability." : $"{report.Vulnerabilities.Count} known vulnerabilities ({counted})")
            + (acceptedCount > 0 ? $", {acceptedCount} of them accepted." : report.Vulnerabilities.Count == 0 ? "" : "."));
        if (ImageRemedies.WhatIsLeft(raised) is { Length: > 0 } left)
        {
            yield return Line("INFO", left);
        }

        if (report.Refused)
        {
            yield return Line("ERROR", "The agent makes no container from it: built on this machine, with a vulnerability moderate or worse, and Settings → Security → Image security check set to Block.");
        }
    }

    /// <summary>What Patch does with the image, in the line that ends its report when a patch is offered.</summary>
    public static string PatchOffered(ImageVulnerabilities report) =>
        Line("INFO", report.SystemUpgrade.Length > 0 || report.PythonFixes is { Count: > 0 }
            ? $"Patch builds {report.Patch} from {report.Image}: its system packages upgraded, as a Windows update patches Windows, and the commands you add (a Python library raised), then moves its containers to it."
            : $"Patch builds {report.Patch} with what you change in it (a library taken out, another put in), then moves its containers to it.");

    /// <summary>A line as the agent's log writes one, which the logs view colours by its level.</summary>
    public static string Line(string level, string text) => $"{level,-8} | {text}";

    /// <summary>A package as a line names it: a Python library by its name, a system package with its distribution (<c>openssl (Debian:12)</c>).</summary>
    private static string Named(string package, string ecosystem) => ecosystem == "PyPI" ? package : $"{package} ({ecosystem})";

    /// <summary>How many packages were read, Python libraries and system packages apart.</summary>
    private static string Counted(IReadOnlyList<ImagePackage> packages)
    {
        var python = packages.Count(package => package.Ecosystem == "PyPI");
        var system = packages.Count - python;
        return string.Join(" and ", new[]
        {
            python > 0 ? $"{python} Python librar{(python == 1 ? "y" : "ies")}" : "",
            system > 0 ? $"{system} system package{(system == 1 ? "" : "s")} ({packages.First(package => package.Ecosystem != "PyPI").Ecosystem})" : "",
        }.Where(part => part.Length > 0));
    }
}
