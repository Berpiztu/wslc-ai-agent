using System.Globalization;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>A line of a log with the moment it was written, for logs of several sources to be put in order and drawn alike (<c>LogsView</c>'s Backups, <see cref="LogLines.Entry"/>).</summary>
/// <param name="Source">Whose it is: the container, the project; empty in one container's own log.</param>
/// <param name="Kind"><see cref="LogLines.Output"/> or <see cref="LogLines.Backup"/>.</param>
/// <param name="Text">The line as it was written, after its time.</param>
public sealed record TimedLine(DateTimeOffset At, string Source, string Kind, string Text);

/// <summary>
/// The backup and restore lines a container's log, or a project's, shows among
/// what its containers wrote: the agent keeps them apart
/// (<c>/{kind}-backups/log</c>), each with the time it was written on the
/// agent's machine, as this client's clock reads it.
/// </summary>
public static class BackupLogLines
{
    /// <summary>A container's: its own backups and restores, alone or driven by its project's.</summary>
    public static async Task<IReadOnlyList<TimedLine>> OfContainerAsync(WslcAgentApi api, string container) =>
        [.. Timed(await api.GetBackupLogAsync(BackupKind.Container.Key, container), "")];

    /// <summary>A project's: its own backups and restores and each of its containers', each said by whose it is.</summary>
    public static async Task<IReadOnlyList<TimedLine>> OfProjectAsync(WslcAgentApi api, string project, IReadOnlyCollection<string> containers)
    {
        var own = Timed(await api.GetBackupLogAsync(BackupKind.Project.Key, project), project);
        var theirs = new List<TimedLine>();
        foreach (var container in containers)
        {
            theirs.AddRange(Timed(await api.GetBackupLogAsync(BackupKind.Container.Key, container), container));
        }

        return [.. own.Concat(theirs).OrderBy(line => line.At)];
    }

    /// <summary>The lines as the agent gives them (<c>time | line</c>), each by its time and <paramref name="whose"/>; one with no time it can read is left out.</summary>
    private static IEnumerable<TimedLine> Timed(IEnumerable<string> lines, string whose)
    {
        foreach (var line in lines)
        {
            var parts = line.Split(" | ", 2);
            if (parts.Length == 2 && DateTime.TryParseExact(parts[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var at))
            {
                yield return new TimedLine(new DateTimeOffset(at), whose, LogLines.Backup, parts[1]);
            }
        }
    }
}
