using System.Globalization;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// A project as one row: what its head row and its card say of it in the
/// Containers list, and what its card says on the dashboard. A project has no
/// row of its own on the agent, which lists containers: it is made here from
/// its containers, in one place, so the two screens never tell it apart.
/// </summary>
public static class ProjectRow
{
    /// <summary>
    /// The row that stands for a project: no container of its own, its name
    /// the project's, how many of its containers run in the place of an
    /// image, running when one of them is, and their CPU and memory share
    /// added up. Its id is its key in the list
    /// (<see cref="ProjectVerbs.RowPrefix"/>).
    /// </summary>
    /// <param name="runningOnly">The list shows only what runs: the count is not said out of a total it does not show.</param>
    public static ContainerSummary Of(string name, IReadOnlyList<ContainerSummary> members, bool runningOnly = false)
    {
        var running = members.Count(member => member.IsRunning);
        var measured = members.Where(member => member.CpuPercent.Length > 0).ToList();
        return new ContainerSummary(ProjectVerbs.RowPrefix + name, name,
            runningOnly ? $"{running} running" : $"{running} of {members.Count} running",
            running > 0 ? "running" : "exited", "", "", "", "", [], "", "", "", "",
            CpuPercent: measured.Count > 0 ? Sum(measured, member => member.CpuPercent) : "",
            MemPercent: measured.Count > 0 ? Sum(measured, member => member.MemPercent) : "",
            Project: name);
    }

    /// <summary>
    /// How a project is, as the class its line is coloured by, round its
    /// cards in the list and round its card on the dashboard: every container
    /// running, none, or some. While something is being done to it it is on
    /// its way from one to the other, and is drawn as some.
    /// </summary>
    /// <param name="containers">Its containers: a run still on its way is none yet, and is not given.</param>
    public static string Line(IReadOnlyCollection<ContainerSummary> containers, bool working)
    {
        if (working)
        {
            return "wslc-bracket-partial";
        }

        var running = containers.Count(container => container.IsRunning);
        return running == 0 ? "wslc-bracket-stopped" : running == containers.Count ? "wslc-bracket-running" : "wslc-bracket-partial";
    }

    /// <summary>
    /// Something is being done to the project that this client knows of
    /// wherever it is shown: a verb going through its containers, its own
    /// down, or a verb, a recreate, an image update, a backup or a restore
    /// on one of its containers. What is done to a container of a project is done to the
    /// project, which shows it as its own work.
    /// </summary>
    public static bool Working(string project, IEnumerable<ContainerSummary> members, ProjectVerbs verbs, BusyRows busy) =>
        verbs.IsGoing(project)
        || busy.IsBusy(ProjectVerbs.RowPrefix + project)
        || members.Any(member => busy.IsBusy(member.Id) || member.IsRecreating || member.IsUpdating || member.BackingUp is not null);

    /// <summary>What a project is called on screen: its name with <c>-compose</c> after it, which says what it is among rows and cards that are containers.</summary>
    public static string Titled(string name) => $"{name}-compose";

    /// <summary>
    /// What a project's dial says in full, for whoever rests the pointer on
    /// it: the measure of its containers added up, and under it each running
    /// container with its own, a line each, so the one that takes the most
    /// is found without opening the group.
    /// </summary>
    public static string Usage(string project, string measure, string total, IEnumerable<ContainerSummary> members, Func<ContainerSummary, string> reading)
    {
        var running = members.Where(member => member.IsRunning).ToList();
        return running.Count == 0
            ? $"{measure}: no container of {project} is running"
            : $"{measure} of {project}: {Readings.Dash(total)}\n{string.Join("\n", running.Select(member => $"{member.Name} {reading(member)}"))}";
    }

    /// <summary>A container's CPU, as its line under a project's CPU dial says it.</summary>
    public static string Cpu(ContainerSummary member) => member.CpuPercent;

    /// <summary>A container's memory, as its line under a project's memory dial says it: its share, and what that is.</summary>
    public static string Memory(ContainerSummary member) => $"{member.MemPercent} ({Readings.MemoryUsed(member.MemUsage)})";

    /// <summary>Percentages as <c>wslc stats</c> prints them, added up and written the same way.</summary>
    private static string Sum(IEnumerable<ContainerSummary> rows, Func<ContainerSummary, string> reading) =>
        rows.Sum(row => Percents.Parse(reading(row))).ToString("0.00", CultureInfo.InvariantCulture) + "%";
}
