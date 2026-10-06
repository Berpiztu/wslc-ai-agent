using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components;

namespace WslcAgent.UI.Updates;

/// <summary>
/// A native client against its agent, from what the agent says of its API in
/// <c>/health</c> (<see cref="ApiCompatibility"/>). Either side may be the old
/// one: a client not updated after its agent was, or a new client on an agent
/// not updated yet. When they can no longer work together the application waits
/// under <see cref="Blocked"/> (MainLayout's veil); when they still can, the
/// older side is named on the activity line, once per agent level, as a warning
/// that stays until it is closed. The browser is left out: the agent serves it
/// its own screens, so it is never behind (<see cref="AgentBuild"/> reloads it).
/// </summary>
public sealed class ApiCompatibilityWatch(ActivityLine activity)
{
    private int _warnedFor;

    /// <summary>The two can no longer work together: which side has to be updated; null while they can.</summary>
    public ApiMatch? Blocked { get; private set; }

    /// <summary>The agent's version as it said it, for the veil's words.</summary>
    public string AgentVersion { get; private set; } = "";

    public event Action? Changed;

    /// <summary>What the agent has just said about itself, from wherever the application was already asking.</summary>
    public void Seen(HealthResponse? health)
    {
        if (health is null || OperatingSystem.IsBrowser())
        {
            return;
        }

        var match = ApiCompatibility.Compare(health);
        AgentVersion = health.Version;
        ApiMatch? blocked = match is ApiMatch.ClientTooOld or ApiMatch.AgentTooOld ? match : null;
        if (blocked != Blocked)
        {
            Blocked = blocked;
            Changed?.Invoke();
        }

        if (match is ApiMatch.ClientBehind or ApiMatch.AgentBehind && _warnedFor != health.ApiLevel)
        {
            _warnedFor = health.ApiLevel;
            activity.Warn(match == ApiMatch.ClientBehind
                ? $"The agent ({health.Version}) speaks a newer API than this client: it still works, but update the client to have everything."
                : $"The agent ({health.Version}) is older than this client: it still works, but update the agent on its machine to have everything.");
        }
    }
}
