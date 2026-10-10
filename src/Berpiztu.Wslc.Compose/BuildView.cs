using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Berpiztu.Wslc.Compose;

/// <summary>
/// The builds of a job drawn the way a Compose user knows them: how far
/// they are on the first line, then a line a step, the one under way in bold
/// with its time running and the last things it printed in grey under it, a
/// finished one in blue with the time it took; and at the end the images
/// built, each with its tick.
/// <para>
/// It is made from what <c>wslc build --progress plain</c> prints, which the
/// agent keeps as printed in the job's log: <c>[stage n/m] INSTRUCTION</c>
/// when a step begins, the same bracket with <c>CACHED</c> when it was taken
/// from an earlier build, <c>FROM image</c> with no bracket for an image
/// another stage copies from, <c>  [stage] text</c> for what a step prints,
/// <c>exporting to image</c> and its <c>  | part</c> lines at the end.
/// <c>wslc</c> says neither when a step ends nor how long it took: a step is
/// over when the next one begins, and its time is the time between the two
/// as this command saw them arrive, which is as fine as the log is read
/// often.
/// </para>
/// While the builds last nothing of them is printed for good: they are a
/// piece of the screen (<see cref="Live"/>) that whoever follows the job
/// draws again at every reading, the first line and under it as many of the
/// last ones as the window has room for. Once they are over the whole of it
/// is printed, every line, to stay and to scroll back through.
/// </summary>
internal sealed partial class BuildView(IReadOnlyDictionary<string, string> services)
{
    public const string Failed = "ERROR";
    public const string Cancelled = "CANCELED";

    private const string Lead = " => ";
    private const string Under = " => => ";

    /// <summary>How the line of a step with no bracket begins, and what follows it when the step was cached.</summary>
    private const string From = "FROM ";
    private const string CachedAfter = " CACHED";

    /// <summary>The lines of a running step's output shown under it, as Compose shows them.</summary>
    private const int Tail = 6;

    /// <summary>The lines of a step's output kept, to show why when it fails: what scrolled under it is gone by then.</summary>
    private const int Kept = 20;

    private readonly Stopwatch _clock = new();
    private readonly List<Entry> _entries = [];
    private readonly List<string> _built = [];
    private string _image = "";
    private Entry? _step;
    private Entry? _part;

    /// <summary>A build is being read: the lines that come are its output.</summary>
    public bool IsOpen => _image.Length > 0;

    /// <summary>
    /// A build begins: of an image, or of several at once, their names
    /// apart by commas, when the file's services are built from the same
    /// thing and one build makes the image all of them take.
    /// </summary>
    public void Open(string images)
    {
        _clock.Start();
        _image = images;
    }

    /// <summary>A line of the job's own that belongs with the build under way: the command it runs.</summary>
    public void Note(string text) => _entries.Add(new Entry("", text, Terminal.Gray));

    /// <summary>A line the build printed.</summary>
    public void Take(string printed)
    {
        var line = printed.TrimEnd('\r');
        if (string.IsNullOrWhiteSpace(line))
        {
            // The padding a count that writes over itself clears its line with: nothing to show.
            return;
        }

        if (Begun().Match(line) is { Success: true } begun)
        {
            var (inside, what) = (begun.Groups["inside"].Value, begun.Groups["what"].Value);
            if (what != "CACHED" || !Cached(inside))
            {
                Begin(inside, $"[{Service} {inside}] {what}");
            }
        }
        else if (line == "exporting to image")
        {
            Begin("", $"[{Service}] {line}");
        }
        else if (line.StartsWith(From, StringComparison.Ordinal))
        {
            // An image another stage copies from: a step of its own, with no bracket, named by its
            // whole line, which is said again with CACHED after it when it was already there.
            if (!line.EndsWith(CachedAfter, StringComparison.Ordinal) || !Cached(line[..^CachedAfter.Length]))
            {
                Begin(line, $"[{Service}] {line}");
            }
        }
        else if (_step is not null && Part().Match(line) is { Success: true } part)
        {
            EndPart();
            _entries.Add(_part = new Entry(Under, part.Groups["text"].Value, Terminal.Blue) { Started = _clock.Elapsed });
        }
        else if (line.StartsWith("WARNING:", StringComparison.Ordinal))
        {
            _entries.Add(new Entry("", $"⚠ {line["WARNING:".Length..].Trim()}", Terminal.Yellow));
        }
        else if (line.StartsWith("process ", StringComparison.Ordinal))
        {
            // What says the step failed. What follows it, the Dockerfile's lines round the one that failed, goes as it comes.
            Stop(Failed);
            _entries.Add(new Entry("", line, Terminal.Red));
        }
        else if (_step is { } step)
        {
            // What the step printed. A line of it that came in pieces (a count that writes over itself,
            // as a package manager's does) has only its first piece marked as the step's: any line that
            // is nothing else, while a step runs, is that step's.
            var text = Clean(Printed().Match(line) is { Success: true } output ? output.Groups["text"].Value : line);
            if (text.Length > 0)
            {
                step.Output.Enqueue(text);
                while (step.Output.Count > Kept)
                {
                    step.Output.Dequeue();
                }
            }
        }
        else
        {
            _entries.Add(new Entry("", line, ""));
        }
    }

    /// <summary>
    /// The build of the image is over. Well: the next one, if there is one,
    /// goes on under it. Or <paramref name="stopped"/>, as
    /// <see cref="Failed"/> or <see cref="Cancelled"/>: the step it was on is
    /// marked with it and the builds end there, printed.
    /// </summary>
    public void Close(string? stopped = null)
    {
        if (!IsOpen)
        {
            return;
        }

        if (stopped is null)
        {
            End();
            _built.AddRange(Images);
            _image = "";
            return;
        }

        Stop(stopped);
        _image = "";
        Print(stopped);
    }

    /// <summary>The builds are over: all of it printed, how long they took, and the images there are for them, each with its tick.</summary>
    public void Sum()
    {
        if (_entries.Count == 0)
        {
            return;
        }

        Print("FINISHED");
        if (_built.Count == 0)
        {
            return;
        }

        Terminal.Line($"[+] build {_built.Count}/{_built.Count}");
        var width = _built.Max(image => image.Length);
        foreach (var image in _built)
        {
            Terminal.Line($" {Terminal.Tint("✔", Terminal.Green)} Image {image.PadRight(width)}  {Terminal.Tint("Built", Terminal.Green)}");
        }

        _built.Clear();
    }

    /// <summary>The builds as they stand, for the piece of the screen that is drawn again: the first line, and the last ones the window has room for under it. Nothing when there are none under way.</summary>
    public IReadOnlyList<string> Live()
    {
        if (_entries.Count == 0)
        {
            return [];
        }

        // One line less than the window has: the cursor stands on the one after the last.
        var room = Math.Max(2, Terminal.Height - 2);
        return [Head(""), .. Body(_clock.Elapsed, live: true).TakeLast(room)];
    }

    /// <summary>The images the build under way makes.</summary>
    private string[] Images => _image.Split(", ", StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Who the build's lines are said by: the service of its image, the first one's when it makes several.</summary>
    private string Service => Images is [var first, ..] ? services.GetValueOrDefault(first, first) : _image;

    private string Head(string ended) =>
        $"[+] Building {Seconds(_clock.Elapsed)}s ({_entries.Count(entry => entry is { Inside: not null, Ended: not null })}/{_entries.Count(entry => entry.Inside is not null)}) {ended}".TrimEnd();

    /// <summary>Every line of the builds, in the order they came; with <paramref name="live"/>, the running step's last output under it.</summary>
    private IEnumerable<string> Body(TimeSpan now, bool live)
    {
        foreach (var entry in _entries)
        {
            var running = entry.Started is not null && entry.Ended is null;
            var label = entry.Mark.Length > 0 ? $"{entry.Mark} {entry.Text}" : entry.Text;
            var time = entry.Started is { } started ? (entry.Ended ?? now) - started : (TimeSpan?)null;
            yield return Row(entry.Lead, label, time, running ? Terminal.Bold : entry.Style);
            var printed = live && running ? entry.Output.TakeLast(Tail) : entry.Mark == Failed ? entry.Output : Enumerable.Empty<string>();
            foreach (var text in printed)
            {
                yield return Row(Under, $"# {text}", null, Terminal.Gray);
            }
        }
    }

    /// <summary>All of it printed for good, under how it ended, and forgotten: what comes next begins a piece of its own.</summary>
    private void Print(string ended)
    {
        _clock.Stop();
        Terminal.Line(Head(ended));
        foreach (var line in Body(_clock.Elapsed, live: false))
        {
            Terminal.Line(line);
        }

        _entries.Clear();
        _clock.Reset();
    }

    /// <summary>
    /// The step named so was taken from an earlier build: marked, with no
    /// time of its own. It need not be the one under way: steps of two
    /// stages come mixed, and one is said to be cached after the next has
    /// begun. False when no step has that name.
    /// </summary>
    private bool Cached(string inside)
    {
        if (_entries.LastOrDefault(entry => entry.Inside == inside && entry.Mark.Length == 0) is not { } step)
        {
            return false;
        }

        step.Mark = "CACHED";
        step.Ended = step.Started;
        if (step == _step)
        {
            EndPart();
            _step = null;
        }

        return true;
    }

    private void Begin(string inside, string label)
    {
        End();
        _entries.Add(_step = new Entry(Lead, label, Terminal.Blue) { Inside = inside, Started = _clock.Elapsed });
    }

    /// <summary>The running step is over, and the part of it that was under way with it.</summary>
    private void End()
    {
        EndPart();
        if (_step is { } step)
        {
            step.Ended = _clock.Elapsed;
            _step = null;
        }
    }

    private void EndPart()
    {
        if (_part is { } part)
        {
            part.Ended = _clock.Elapsed;
            _part = null;
        }
    }

    /// <summary>The running step did not get to its end: marked so, in red when it failed, with what it printed kept under it, which is where the reason is.</summary>
    private void Stop(string stopped)
    {
        if (_step is { } step)
        {
            step.Mark = stopped;
            step.Style = stopped == Failed ? Terminal.Red : Terminal.Yellow;
        }

        End();
    }

    /// <summary>
    /// A line of the build: what it says at the left, cut where the window
    /// ends, and its time at the right edge. Never longer than the window:
    /// a line that wraps is two, and the piece of the screen is drawn again
    /// by counting lines.
    /// </summary>
    private static string Row(string lead, string text, TimeSpan? time, string style)
    {
        var clock = time is { } taken ? $" {Seconds(taken),6}s" : "";
        var room = Math.Max(0, Terminal.Width - lead.Length - clock.Length);
        return Terminal.Tint(lead + (text.Length > room ? text[..room] : text.PadRight(clock.Length > 0 ? room : 0)) + clock, style);
    }

    private static string Seconds(TimeSpan time) => Math.Max(0, time.TotalSeconds).ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>What a step printed, fit for one line of a terminal: a tab or an escape in it would move what is drawn after it.</summary>
    private static string Clean(string text) => string.Concat(text.Select(letter => char.IsControl(letter) ? ' ' : letter)).Trim();

    /// <summary><c>[stage n/m] INSTRUCTION</c>, or the same bracket and <c>CACHED</c>; a Dockerfile of one stage has no stage's name.</summary>
    [GeneratedRegex(@"^\[(?<inside>[^\]]*\d+/\d+)\] (?<what>.*)$")]
    private static partial Regex Begun();

    /// <summary><c>  [stage] text</c>: what a step printed.</summary>
    [GeneratedRegex(@"^  \[[^\]]*\] ?(?<text>.*)$")]
    private static partial Regex Printed();

    /// <summary><c>  | text</c>: a part of the export.</summary>
    [GeneratedRegex(@"^  \| (?<text>.*)$")]
    private static partial Regex Part();

    /// <summary>
    /// A line of the builds: a step, a part of one, or something said beside
    /// them (the command a build runs, a warning, what follows a failure).
    /// </summary>
    /// <param name="lead">What it begins with: a step's arrow, a part's two, nothing for the rest.</param>
    /// <param name="text">What it says.</param>
    /// <param name="style">Its colour; a step's and a part's is the one they have once they are over, and while they run they are bold.</param>
    private sealed class Entry(string lead, string text, string style)
    {
        public string Lead { get; } = lead;

        public string Text { get; } = text;

        public string Style { get; set; } = style;

        /// <summary>What a step's bracket holds, <c>stage n/m</c>: how the line that says it was cached names it. Empty for the export; null for what is no step.</summary>
        public string? Inside { get; init; }

        /// <summary>When it began, on the builds' clock; null for what takes no time.</summary>
        public TimeSpan? Started { get; set; }

        public TimeSpan? Ended { get; set; }

        /// <summary>What stands before it: <c>CACHED</c>, or how it stopped.</summary>
        public string Mark { get; set; } = "";

        public Queue<string> Output { get; } = new();
    }
}
