using MudBlazor;
using WslcAgent.ApiClient;

namespace WslcAgent.UI.Components;

/// <summary>
/// What the user's own verbs are doing, said on the title bar's second line
/// instead of in toasts that cover the bar and its buttons: "Stopping web…"
/// while a verb runs, "web: stop ok" for a moment when it is over, and each
/// failure until the user closes it. The line is one line however much there
/// is: what runs and the failures not closed are its list, which opens over
/// the page. With nothing to say the line shows the page's own (its search, or
/// its subtitle).
/// A failure while the agent does not answer is not said: the layout already
/// says that once, over the screen, as <see cref="ErrorsStaySnackbar"/> does.
/// </summary>
public sealed class ActivityLine(AgentLink link) : IDisposable
{
    /// <summary>How long the result of a verb that worked stays before the line goes back to the page's own.</summary>
    private static readonly TimeSpan ResultFor = TimeSpan.FromSeconds(3);

    /// <summary>Verbs whose button is the application's blue: their result says so in that blue, so the line and the control that caused it read as one thing.</summary>
    private static readonly string[] BlueVerbs = ["stop"];

    /// <summary>The failures kept: the oldest goes past this many, so the list never grows without end.</summary>
    private const int FailuresKept = 20;

    private readonly List<ActivityWork> _running = [];

    private readonly List<ActivityNote> _failures = [];

    private CancellationTokenSource? _fading;

    /// <summary>The last verb that worked or the last note, for a moment; failures are <see cref="Failures"/>.</summary>
    public ActivityNote? Result { get; private set; }

    /// <summary>The verb started last of those still running; null when none runs.</summary>
    public ActivityWork? Running => _running.Count > 0 ? _running[^1] : null;

    /// <summary>The verbs running, the last started first.</summary>
    public IEnumerable<ActivityWork> AllRunning => Enumerable.Reverse(_running);

    /// <summary>The failures not closed yet, the newest first.</summary>
    public IReadOnlyList<ActivityNote> Failures => _failures;

    /// <summary>What the line's list holds: the verbs running and the failures not closed.</summary>
    public int Listed => _running.Count + _failures.Count;

    /// <summary>
    /// What the closed line shows: a verb running, else a result of a moment ago,
    /// else the newest failure. A running verb is drawn by the view from <see cref="Running"/>.
    /// </summary>
    public ActivityNote? Shown => Result ?? (_failures.Count > 0 ? _failures[0] : null);

    /// <summary>There is something to say: the page's own line gives way to it.</summary>
    public bool Speaking => Running is not null || Shown is not null;

    public event Action? Changed;

    /// <summary>A verb starts: its line, "Stopping web…", until it ends. Failures not closed stay in the list behind it.</summary>
    public ActivityWork Begin(string text)
    {
        var work = new ActivityWork(this, text);
        _running.Add(work);
        Changed?.Invoke();
        return work;
    }

    /// <summary>A verb that ran where the line could not be seen (a dialog over it): what it came to, for a moment, in its button's colour.</summary>
    public void Done(string text, string verb = "") => Show(new ActivityNote(text, ToneOf(verb)));

    /// <summary>Says something about a verb that did not run (it was not the moment for it), for a moment, in the info tone.</summary>
    public void Say(string text) => Show(new ActivityNote(text, Color.Info));

    /// <summary>A verb that failed before it could start (what it needed could not be read): said until it is closed.</summary>
    public void Fail(string text) => Show(new ActivityNote(text, Color.Error));

    /// <summary>Closes a failure the user has read.</summary>
    public void Dismiss(ActivityNote failure)
    {
        _failures.Remove(failure);
        Changed?.Invoke();
    }

    /// <summary>Closes every failure.</summary>
    public void DismissAll()
    {
        _failures.Clear();
        Changed?.Invoke();
    }

    /// <summary>The colour a verb that worked is said in: its button's.</summary>
    public static Color ToneOf(string verb) =>
        BlueVerbs.Contains(verb, StringComparer.OrdinalIgnoreCase) ? Color.Primary : Color.Success;

    internal void Update() => Changed?.Invoke();

    internal void End(ActivityWork work, ActivityNote? result)
    {
        _running.Remove(work);
        if (result is null)
        {
            Changed?.Invoke();
            return;
        }

        Show(result);
    }

    /// <summary>A result takes the line: a failure joins the list until it is closed, anything else is said for a moment.</summary>
    private void Show(ActivityNote result)
    {
        if (result.Tone == Color.Error)
        {
            if (link.Online)
            {
                _failures.Insert(0, result);
                if (_failures.Count > FailuresKept)
                {
                    _failures.RemoveAt(_failures.Count - 1);
                }

                // The failure is the newest thing to say: a result of a moment ago gives way to it.
                _fading?.Cancel();
                Result = null;
            }

            Changed?.Invoke();
            return;
        }

        _fading?.Cancel();
        Result = result;
        _ = FadeAsync(result);
        Changed?.Invoke();
    }

    /// <summary>The result of a verb that worked goes after a moment, unless another took its place meanwhile.</summary>
    private async Task FadeAsync(ActivityNote result)
    {
        var fading = _fading = new CancellationTokenSource();
        try
        {
            await Task.Delay(ResultFor, fading.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (Result == result)
        {
            Result = null;
            Changed?.Invoke();
        }
    }

    public void Dispose() => _fading?.Cancel();
}

/// <summary>What the line says of a verb that ended. A class, not a record: two failures with the same text are two failures, each closed on its own.</summary>
public sealed class ActivityNote(string text, Color tone)
{
    public string Text { get; } = text;

    public Color Tone { get; } = tone;
}

/// <summary>
/// One verb running, as the activity line shows it; ended once, by
/// <see cref="Done"/>, <see cref="Fail"/> or <see cref="Quiet"/>, and a second
/// end is nothing. Begun with <c>using</c>: a verb that ends by an exception
/// nobody foresaw, or that says nothing, leaves the line all the same instead
/// of turning on it for good.
/// </summary>
public sealed class ActivityWork : IDisposable
{
    private readonly ActivityLine _line;

    private bool _ended;

    internal ActivityWork(ActivityLine line, string text)
    {
        _line = line;
        Text = text;
    }

    public string Text { get; private set; }

    /// <summary>The line repainted where it stands: "Removing 2 of 5".</summary>
    public void Set(string text)
    {
        if (Text != text)
        {
            Text = text;
            _line.Update();
        }
    }

    /// <summary>It worked: said for a moment, in the colour of the button that ran it.</summary>
    public void Done(string text, string verb = "") => End(new ActivityNote(text, ActivityLine.ToneOf(verb)));

    /// <summary>It failed: kept in the line's list until the user closes it.</summary>
    public void Fail(string text) => End(new ActivityNote(text, Color.Error));

    /// <summary>It ended with nothing to say (the user cancelled, or another screen says it).</summary>
    public void Quiet() => End(null);

    /// <summary>Leaves the line if it has not ended yet, saying nothing.</summary>
    public void Dispose() => Quiet();

    private void End(ActivityNote? result)
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        _line.End(this, result);
    }
}
