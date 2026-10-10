using System.Text.RegularExpressions;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace Berpiztu.Wslc.Compose;

/// <summary>
/// A job of the agent on a project (its save, a build of its images),
/// followed to its end: the agent does the work and keeps its log, and this
/// prints each line as it comes. On a terminal the builds in it are drawn
/// as a build is (<see cref="BuildView"/>): a piece of the screen that is
/// drawn again at every reading, and printed whole once they are over. The work is the
/// agent's, so the first Ctrl+C cancels it there, as the cross of its row on
/// the Containers page does, and the end the agent gives is waited for; a
/// second one leaves it to the agent and comes back.
/// </summary>
/// <param name="services">The service each image of the file is built for, by the image's name: a build's lines are said by their service.</param>
internal sealed partial class JobFollower(WslcAgentApi api, Interrupt interrupt, IReadOnlyDictionary<string, string> services)
{
    /// <summary>How often the log is read: often enough for a step's time, counted here, to be worth saying.</summary>
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(250);

    /// <summary>How the agent's job says a build begins, before the image's name (ProjectRunner). A job that said it otherwise would have its build printed as it comes, not drawn.</summary>
    private const string Building = "Building ";

    /// <summary>The builds as they are drawn; null where the output is no terminal, and every line goes as it is.</summary>
    private readonly BuildView? _builds = Terminal.IsLive ? new BuildView(services) : null;

    /// <summary>How many lines of the screen are the builds' piece, to be written over at the next reading.</summary>
    private int _drawn;

    /// <summary>How many of the log's lines are printed, and the last of them: where the next reading goes on from.</summary>
    private int _printed;
    private string _last = "";

    private int _cancelAsked;

    public async Task<int> FollowAsync(ProjectJob job)
    {
        var cancelling = false;
        interrupt.TakeNext(() => Volatile.Write(ref _cancelAsked, 1));
        Terminal.Cursor(shown: false);
        try
        {
            while (true)
            {
                // Everything of a reading reaches the screen at once: the piece that is drawn again is
                // never seen half written.
                Terminal.Hold();
                Rewind();
                Print(job.Log);
                if (job.State != ProjectJob.Running)
                {
                    return await EndedAsync(job);
                }

                if (!cancelling && Volatile.Read(ref _cancelAsked) == 1)
                {
                    cancelling = true;
                    Terminal.Warning($"Cancelling {job.Name}… Ctrl+C again leaves it to the agent.");
                    await api.CancelContainerLaunchAsync(job.Id, interrupt.Token);
                }

                Draw();
                Terminal.Release();
                await Task.Delay(Every, interrupt.Token);
                try
                {
                    job = await api.GetProjectJobAsync(job.Id, interrupt.Token);
                }
                catch (AgentApiException ex) when (ex.StatusCode == 404)
                {
                    // The agent forgets a job that went well a moment after it ends, and keeps the
                    // others: one it no longer knows is one that ended well.
                    Terminal.Hold();
                    Rewind();
                    _builds?.Sum();
                    Terminal.Done($"{job.Name}: done");
                    return Program.Ok;
                }
            }
        }
        finally
        {
            // However it ends, what was being drawn is not left on the screen as if it still were.
            Terminal.Hold();
            Rewind();
            Terminal.Release();
            Terminal.Cursor(shown: true);
        }
    }

    /// <summary>Back to the first line of the builds' piece: what is printed next is written over it.</summary>
    private void Rewind()
    {
        Terminal.Rewind(_drawn);
        _drawn = 0;
    }

    /// <summary>Draws the builds as they stand.</summary>
    private void Draw()
    {
        foreach (var line in _builds?.Live() ?? [])
        {
            Terminal.Line(line);
            _drawn++;
        }
    }

    private async Task<int> EndedAsync(ProjectJob job)
    {
        // A job that ends with its builds still on the screen has them printed before how it ended is said.
        _builds?.Sum();
        switch (job.State)
        {
            case ProjectJob.Done:
                Terminal.Done($"{job.Name}: done");
                return Program.Ok;
            case ProjectJob.Cancelled:
                Terminal.Warning($"{job.Name}: cancelled.");
                return Program.Interrupted;
            default:
                // Why is the log's last line, already printed as the error it is.
                Terminal.Error($"{job.Name}: failed.");
                await DismissAsync(job);
                return Program.Failed;
        }
    }

    /// <summary>
    /// A failed job stays as a row of the Containers page until someone
    /// dismisses it, so that whoever started it reads why. Whoever started
    /// this one has just read it here, whole: the row is let go, or every
    /// failed run of a script would leave one behind.
    /// </summary>
    private async Task DismissAsync(ProjectJob job)
    {
        try
        {
            await api.DismissContainerLaunchAsync(job.Id, interrupt.Token);
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            // The failure is the job's, already said; a row that stays is the page's to dismiss.
        }
    }

    private void Print(IReadOnlyList<string> log)
    {
        for (var index = Unprinted(log); index < log.Count; index++)
        {
            Show(log[index]);
        }

        _printed = log.Count;
        _last = log.Count > 0 ? log[^1] : "";
    }

    /// <summary>
    /// Where the lines not printed yet begin. The agent keeps the last lines
    /// of a long log and drops the first ones, so a count alone would skip
    /// lines once it does: the last line printed is looked for.
    /// </summary>
    private int Unprinted(IReadOnlyList<string> log)
    {
        if (_printed == 0)
        {
            return 0;
        }

        if (_printed <= log.Count && log[_printed - 1] == _last)
        {
            return _printed;
        }

        for (var index = log.Count - 1; index >= 0; index--)
        {
            if (log[index] == _last)
            {
                return index + 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// A line of the job's log as a terminal reads it. The agent stamps its
    /// own with the time and the level, as its log's lines are: a terminal
    /// wants neither written out, and the level is how the line looks. A
    /// step stands out, the command it ran stands back, a warning and an
    /// error are said as such. What a build printed has no stamp: it is
    /// drawn as a build where there is a terminal to draw on, and goes as it
    /// is where there is none.
    /// </summary>
    private void Show(string line)
    {
        if (Stamped().Match(line) is not { Success: true } stamped)
        {
            if (_builds is { IsOpen: true })
            {
                _builds.Take(line);
            }
            else
            {
                Terminal.Line(line);
            }

            return;
        }

        var (level, text) = (stamped.Groups["level"].Value, stamped.Groups["text"].Value);
        if (_builds is not null && Drawn(_builds, level, text))
        {
            return;
        }

        switch (level)
        {
            case "DEBUG":
                Terminal.Faint(text);
                break;
            case "WARNING":
                Terminal.Warning(text);
                break;
            case "ERROR":
                Terminal.Error(text);
                break;
            default:
                Terminal.Step(text);
                break;
        }
    }

    /// <summary>
    /// What a line of the job's own means to the builds being drawn, and
    /// whether that was all there was to it. The step that begins a build
    /// opens one, and is not printed: the build's own lines say it. The
    /// command the build runs comes next and is a line of the build. Any
    /// other line of the job's own ends the build under way, failed when it
    /// is the error; and the first step after the builds has them printed
    /// and summed up.
    /// </summary>
    private static bool Drawn(BuildView builds, string level, string text)
    {
        if (level == "INFO" && text.StartsWith(Building, StringComparison.Ordinal))
        {
            builds.Close();
            builds.Open(text[Building.Length..]);
            return true;
        }

        if (level == "DEBUG" && builds.IsOpen)
        {
            builds.Note(text);
            return true;
        }

        builds.Close(level switch { "INFO" => null, "WARNING" => BuildView.Cancelled, _ => BuildView.Failed });
        if (level == "INFO")
        {
            builds.Sum();
        }

        return false;
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} \| (?<level>\w+) *\| (?<text>.*)$")]
    private static partial Regex Stamped();
}
