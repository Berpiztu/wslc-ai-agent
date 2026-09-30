using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// Which WSLC session the app is on and whether it is running, as the session
/// panel last read it. A screen listens here instead of asking: with the
/// session stopped there is nothing to list, and a page that went on polling
/// would open the session again — the very thing it was stopped for (the
/// System page's Compact VHDX needs it down). Starting it again wakes every
/// screen at once, without reloading the application.
/// </summary>
public sealed class SessionState
{
    /// <summary>True until the panel says otherwise, so a page loads normally on the way in.</summary>
    public bool Active { get; private set; } = true;

    /// <summary>The session every command targets, by name; empty only before the panel has read once.</summary>
    public string Selected { get; private set; } = "";

    /// <summary>Raised when the session changed, or started or stopped.</summary>
    public event Action? Changed;

    /// <summary>The panel publishes what it read; nothing is raised when nothing changed.</summary>
    public void Set(SessionsResponse sessions)
    {
        // The session the agent targets, and no other: another one running is
        // not this one running, and a page that listed on that had nothing to
        // list.
        var active = sessions.Sessions.Any(s => s.Active && s.Name == sessions.Selected);
        if (active == Active && sessions.Selected == Selected)
        {
            return;
        }

        Active = active;
        Selected = sessions.Selected;
        Changed?.Invoke();
    }
}
