using System.Net.Http;
using MudBlazor;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// The WSLC sessions as the agent last listed them, and the verbs on them:
/// select one, start it, stop it. Every control that shows the session — the
/// session line of the title bar, the navigation button's colour — reads
/// here, so none holds a copy of the logic and none disagrees with another.
/// What changes is published through <see cref="SessionState"/>, which every
/// screen listens to.
/// </summary>
public sealed class SessionPower(WslcAgentApi api, ISnackbar snackbar, ActivityLine activity, SessionState state, AgentChanges changes)
{
    private Task? _first;

    public IReadOnlyList<SessionInfo> Sessions { get; private set; } = [];

    /// <summary>
    /// The session every command targets, by name. The agent always targets
    /// one: choosing none is choosing the CLI's own store for this user, which
    /// the agent names like any other rather than showing a "default" that is
    /// not a session and answers for whichever happens to be up.
    /// </summary>
    public string Selected { get; private set; } = "";

    public bool Loading { get; private set; }

    /// <summary>A start or a stop is under way.</summary>
    public bool Busy { get; private set; }

    /// <summary>This session, and no other: what is running elsewhere is not this one running.</summary>
    public bool IsActive => Sessions.Any(s => s.Active && s.Name == Selected);

    public SessionInfo? Current => Sessions.FirstOrDefault(s => s.Name == Selected);

    /// <summary>A stopped store of the CLI's own that the agent cannot open: the button says why instead of failing.</summary>
    public bool CanPower => IsActive || Current is not { Active: false, Reserved: true, CanStart: false };

    public string Label => Selected;

    public string PowerTitle => (IsActive, CanPower) switch
    {
        (true, _) => $"Stop {Label}",
        (false, false) => "An admin reserved session cannot be started from the agent. Use an elevated process on that machine, or pick another session.",
        _ => $"Start {Label}",
    };

    /// <summary>The status line: what the selected session is right now.</summary>
    public string Status => (Busy, IsActive, CanPower) switch
    {
        (true, _, _) => "Working…",
        (_, true, _) => "Active",
        (_, false, false) => "Stopped · admin reserved",
        _ => "Stopped",
    };

    /// <summary>The tooltip: which session, and the store it lives in.</summary>
    public string SelectTitle =>
        Current is { Path.Length: > 0 } store ? $"Session: {Selected}\n{store.Path}" : $"Session: {Selected}";

    /// <summary>How many of the running containers the stop question names before it counts the rest.</summary>
    private const int Named = 6;

    /// <summary>
    /// The message every stop asks with: what goes down with the session, by
    /// name, because "every container running in it" is what the user is
    /// trying to find out. The general warning is what is left when the list
    /// cannot be read — a stop is never held up by it.
    /// </summary>
    public static string StopQuestion(string label, IReadOnlyList<string> running) => running.Count switch
    {
        0 => $"Stop {label}? Every container running in it stops with it.",
        <= Named => $"Stop {label}? These stop with it: {string.Join(", ", running)}.",
        _ => $"Stop {label}? These stop with it: {string.Join(", ", running.Take(Named))} and {running.Count - Named} more.",
    };

    /// <summary>Raised whenever the list, the selection or a verb's progress changed.</summary>
    public event Action? Changed;

    /// <summary>
    /// The option label says only what cannot be
    /// seen: the name, and in brackets stopped, or a reserved store of the
    /// CLI's own that the agent cannot start. Active and selected are left
    /// out — the one showing in the closed field is both,
    /// the status beside it says which, and those two words made the option
    /// twice as wide as the application's name above it, which is the width the
    /// whole block has.
    /// </summary>
    public static string OptionLabel(SessionInfo session)
    {
        var state = session switch
        {
            { Active: false, Reserved: true, CanStart: false } => "reserved",
            { Active: false } => "stopped",
            _ => "",
        };

        return state.Length > 0 ? $"{session.Name} ({state})" : session.Name;
    }

    /// <summary>
    /// Read once, when the first control asks, again after the verbs here, and
    /// again when the agent says the session changed. Nothing polls: one
    /// <c>wslc system info</c> per client every five seconds filled the log,
    /// and a clock is not wanted here at all. A stop made on another client, or from a
    /// terminal on the machine, reaches this one as a notice on the change
    /// stream, and only then is the session read.
    /// </summary>
    public Task ReadyAsync() => _first ??= FirstAsync();

    private Task FirstAsync()
    {
        changes.Changed += OnAgentChanged;
        changes.Start();
        return LoadAsync();
    }

    /// <summary>
    /// A notice that touches the session: read it again. Not while a verb of
    /// this client is under way — its own answer brings the list — and without
    /// a word on failure: nobody asked, and the next notice asks again.
    /// </summary>
    private void OnAgentChanged(ChangeNotice notice)
    {
        if (notice.Touches(ChangeNotice.Session) && !Busy)
        {
            _ = ReadAgainAsync();
        }
    }

    private async Task ReadAgainAsync()
    {
        try
        {
            Take(await api.GetSessionsAsync());
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            // The sign-in screen, or the wait for the agent, says what there is to say.
        }
    }

    public async Task LoadAsync()
    {
        Loading = true;
        Changed?.Invoke();
        try
        {
            Take(await api.GetSessionsAsync());
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            // Refused: the sign-in screen takes over and says so itself.
            if (!ex.IsSignInRequired())
            {
                snackbar.Add($"Sessions: {ex.Message}", Severity.Error);
            }
        }
        finally
        {
            Loading = false;
            Changed?.Invoke();
        }
    }

    public async Task SelectAsync(string name)
    {
        using var work = activity.Begin($"Selecting {name}");
        try
        {
            await api.SelectSessionAsync(name);
            Selected = name;
            await LoadAsync();
            work.Done($"{name} selected");
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            work.Fail($"Select session: {ex.Message}");
        }
    }

    /// <summary>
    /// What a start or a stop is doing right now, said on the veil that covers
    /// the whole screen while it lasts; empty when no verb is under way. A
    /// session is the system every container lives in: nothing else is offered
    /// while it goes down or comes up, and the user is told each step instead
    /// of seeing a button that merely stays grey.
    /// </summary>
    public string Phase { get; private set; } = "";

    /// <summary>
    /// The one power verb: stops the session shown as running
    /// — after <paramref name="confirmStop"/>, asked with what goes down with it,
    /// says yes — and starts it when it is stopped, which brings the containers
    /// the restart policy holds back up. The agent's answer is not taken on
    /// trust: the sessions are read again and the session has to be where the
    /// verb said it would be. A failure goes to <paramref name="reportFailure"/>
    /// (title, message, explanation), a dialog that stays until it is closed:
    /// a stop that did not stop once showed as a green "Stopped" and left the
    /// machine with a session nobody could clear without a restart.
    /// </summary>
    public async Task TogglePowerAsync(Func<string, Task<bool>> confirmStop, Func<string, string, string, Task> reportFailure)
    {
        var stopping = IsActive;
        if (stopping && !await confirmStop(await StopQuestionAsync()))
        {
            return;
        }

        var label = Label;
        var title = stopping ? $"{label} did not stop" : $"{label} did not start";
        string? failure = null;
        Busy = true;
        // The veil says each step while the verb lasts; the line says how it ended.
        using var work = activity.Begin(stopping ? $"Stopping {label}" : $"Starting {label}");
        try
        {
            SetPhase(stopping
                ? $"Stopping {label}… This can take a couple of minutes."
                : $"Starting {label}…");
            var result = stopping ? await api.StopSessionAsync(label) : await api.StartSessionAsync(label);

            SetPhase(stopping ? $"Checking that {label} has really stopped…" : $"Checking that {label} is running…");
            Take(await api.GetSessionsAsync());
            if (IsActive == stopping)
            {
                failure = stopping
                    ? $"The agent answered \"{result.Message}\", but {label} is still listed as running."
                    : $"The agent answered \"{result.Message}\", but {label} is not listed as running.";
            }
            else
            {
                work.Done(stopping ? $"{label} stopped." : $"{label} started.", stopping ? "stop" : "start");
            }
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException or TaskCanceledException)
        {
            failure = ex.Message;
            SetPhase("Reading the sessions again…");
            await LoadAsync();
        }
        finally
        {
            Busy = false;
            SetPhase("");
        }

        if (failure is not null)
        {
            // A failure here is a dialog with what to do about it, not a line.
            work.Quiet();
            await reportFailure(title, failure, stopping
                ? "The session may be stuck, and its containers may not answer until it is cleared: wsl --shutdown, or a restart of Windows. The session line shows what wslc reports now."
                : "The session line shows what wslc reports now. The agent's log has the wslc output.");
        }
    }

    private void SetPhase(string phase)
    {
        Phase = phase;
        Changed?.Invoke();
    }

    /// <summary>
    /// The stop question with what runs in the session right now in it. The
    /// verb shows as busy while that is read, so the button cannot be pressed
    /// again before the dialog is up.
    /// </summary>
    private async Task<string> StopQuestionAsync()
    {
        Busy = true;
        Changed?.Invoke();
        try
        {
            var running = await api.GetContainersAsync(all: false);
            return StopQuestion(Label, [.. running.Containers.Select(c => c.Name).Where(name => name.Length > 0)]);
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            // The general warning does the asking instead: the stop itself is still offered.
            return StopQuestion(Label, []);
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Every screen listens through SessionState: the ones that list what lives in the session stop asking for it, or take it up again, without a reload.</summary>
    private void Take(SessionsResponse response)
    {
        Sessions = response.Sessions;
        Selected = response.Selected;
        state.Set(response);
    }
}
