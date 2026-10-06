using MudBlazor;
using WslcAgent.ApiClient;

namespace WslcAgent.UI.Components;

/// <summary>
/// What the user's own verbs are doing, said on the title bar's second line
/// instead of in toasts that cover the bar and its buttons: "Stopping web…"
/// while a verb runs, "web: stop ok" for a moment when it is over, and a
/// failure until the user closes it or runs the next verb. With nothing to
/// say the line shows the page's own (its search, or its subtitle).
/// A failure while the agent does not answer is not said: the layout already
/// says that once, over the screen, as <see cref="ErrorsStaySnackbar"/> does.
/// </summary>
public sealed class ActivityLine(AgentLink link) : IDisposable
{
    /// <summary>How long the result of a verb that worked stays before the line goes back to the page's own.</summary>
    private static readonly TimeSpan ResultFor = TimeSpan.FromSeconds(3);

    /// <summary>Verbs whose button is the application's blue: their result says so in that blue, so the line and the control that caused it read as one thing.</summary>
    private static readonly string[] BlueVerbs = ["stop"];

    private readonly List<ActivityWork> _running = [];

    private CancellationTokenSource? _fading;

    /// <summary>The last verb that ended, while it is still said: what it came to, and in which colour.</summary>
    public ActivityNote? Result { get; private set; }

    /// <summary>The verb started last of those still running; null when none runs.</summary>
    public ActivityWork? Running => _running.Count > 0 ? _running[^1] : null;

    /// <summary>How many other verbs run besides <see cref="Running"/>.</summary>
    public int AlsoRunning => Math.Max(0, _running.Count - 1);

    /// <summary>There is something to say: the page's own line gives way to it.</summary>
    public bool Speaking => Running is not null || Result is not null;

    public event Action? Changed;

    /// <summary>A verb starts: its line, "Stopping web…", until it ends. A failure still on the line is cleared by it.</summary>
    public ActivityWork Begin(string text)
    {
        var work = new ActivityWork(this, text);
        _running.Add(work);
        if (Result is { Tone: Color.Error })
        {
            Result = null;
        }

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
    public void Dismiss()
    {
        Result = null;
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

    /// <summary>A result takes the line: a failure until it is closed, anything else for a moment.</summary>
    private void Show(ActivityNote result)
    {
        _fading?.Cancel();
        _fading = null;
        if (result.Tone != Color.Error || link.Online)
        {
            Result = result;
            if (result.Tone != Color.Error)
            {
                _ = FadeAsync(result);
            }
        }

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

/// <summary>What the line says of a verb that ended.</summary>
public sealed record ActivityNote(string Text, Color Tone);

/// <summary>One verb running, as the activity line shows it; ended once, by <see cref="Done"/> or <see cref="Fail"/>.</summary>
public sealed class ActivityWork
{
    private readonly ActivityLine _line;

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
    public void Done(string text, string verb = "") => _line.End(this, new ActivityNote(text, ActivityLine.ToneOf(verb)));

    /// <summary>It failed: said until the user closes it or runs the next verb.</summary>
    public void Fail(string text) => _line.End(this, new ActivityNote(text, Color.Error));

    /// <summary>It ended with nothing to say (the user cancelled, or another screen says it).</summary>
    public void Quiet() => _line.End(this, null);
}
