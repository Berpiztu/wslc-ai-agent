using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace Berpiztu.Wslc.Compose;

/// <summary>
/// A project's logs on the terminal, as Compose prints them: each line said
/// by its container, the names in a column and each in a colour of its own,
/// so several services writing at once can be told apart. Read once, or
/// followed: the agent is asked again every second for the lines after the
/// last one printed, by its stamp, so each line is printed once.
/// </summary>
/// <param name="services">The services to read; none, the whole project.</param>
/// <param name="prefix">Each line said by its container; false prints the lines alone.</param>
/// <param name="timestamps">Each line with the moment it was written.</param>
internal sealed class ProjectLogStream(WslcAgentApi api, string project, IReadOnlyList<string> services, bool prefix, bool timestamps)
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(1);

    /// <summary>The colours the containers take in turn, as they first write.</summary>
    private static readonly string[] Colours = [Terminal.Cyan, Terminal.Yellow, Terminal.Green, Terminal.Magenta, Terminal.Blue];

    /// <summary>The containers that have written, in the order they first did: a container's place is its colour.</summary>
    private readonly List<string> _containers = [];

    /// <summary>The stamp of the last line printed: the next reading asks for what came after it.</summary>
    private string _last = "";

    /// <summary>How wide the names' column is: the longest name of the project, so the lines stand in one column from the first.</summary>
    private int _width;

    /// <summary>The names the lines will be said by, known before any is written: the column takes its width from them.</summary>
    public void Names(IEnumerable<string> containers) => _width = Math.Max(_width, containers.Select(name => name.Length).DefaultIfEmpty(0).Max());

    /// <summary>
    /// Prints what there is: the last <paramref name="tail"/> lines of each
    /// container (0: all), or, once something has been printed or with
    /// <paramref name="since"/>, the lines after that moment. True when any
    /// line was printed.
    /// </summary>
    public async Task<bool> ReadAsync(int tail, CancellationToken cancellationToken, string since = "")
    {
        var from = _last.Length > 0 ? _last : since;
        var logs = await api.GetProjectLogsAsync(project, from.Length > 0 ? 0 : tail, from, services, cancellationToken);
        foreach (var line in logs.Lines)
        {
            Print(line);
            _last = string.CompareOrdinal(line.Time, _last) > 0 ? line.Time : _last;
        }

        return logs.Lines.Count > 0;
    }

    /// <summary>Follows the logs until <paramref name="until"/> says to stop (asked after each reading) or the command is stopped.</summary>
    public async Task FollowAsync(Func<Task<bool>> until, CancellationToken cancellationToken)
    {
        while (!await until())
        {
            await Task.Delay(Every, cancellationToken);
            await ReadAsync(0, cancellationToken);
        }

        // What was written between the last reading and the end.
        await ReadAsync(0, cancellationToken);
    }

    private void Print(ProjectLogLine line)
    {
        if (!_containers.Contains(line.Container))
        {
            _containers.Add(line.Container);
            _width = Math.Max(_width, line.Container.Length);
        }

        var text = timestamps && line.Time.Length > 0 ? $"{line.Time} {line.Text}" : line.Text;
        Terminal.Line(prefix
            ? $"{Terminal.Tint($"{line.Container.PadRight(_width)}  |", Colours[_containers.IndexOf(line.Container) % Colours.Length])} {text}"
            : text);
    }
}
