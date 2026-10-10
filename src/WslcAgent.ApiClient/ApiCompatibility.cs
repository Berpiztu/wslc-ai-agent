using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.ApiClient;

/// <summary>
/// Whether a client and an agent built apart can work together. A native client
/// (Android, Windows) is installed on its own and lives on while its agent is
/// updated, or the other way round; each side compiles in the level of the API
/// it was built with and the oldest level of the other side it still works with.
/// The agent says its two in <c>/health</c>; the client compares them with its
/// own (<see cref="Compare"/>).
/// <para>
/// The rule (AGENTS.md): every change to <c>/api/v1</c> raises <see cref="Level"/>.
/// One that breaks the clients before it — an endpoint or a field removed or
/// renamed, a field made required — also raises <see cref="MinimumClientLevel"/>
/// to it; one the client cannot do without on an older agent raises
/// <see cref="MinimumAgentLevel"/>. A change that only adds raises neither: the
/// older side keeps working, and is told it is behind.
/// </para>
/// </summary>
public static class ApiCompatibility
{
    /// <summary>The level of the API this build speaks.</summary>
    public const int Level = 4;

    /// <summary>The oldest client level this agent still serves (sent in <c>/health</c>).</summary>
    public const int MinimumClientLevel = 1;

    /// <summary>The oldest agent level this client still works with.</summary>
    public const int MinimumAgentLevel = 1;

    /// <summary>What this client makes of the agent's <c>/health</c>; an agent too old to say its level is not judged.</summary>
    public static ApiMatch Compare(HealthResponse health)
    {
        if (health.ApiLevel <= 0)
        {
            return ApiMatch.Unknown;
        }

        if (Level < health.MinimumClientApiLevel)
        {
            return ApiMatch.ClientTooOld;
        }

        if (health.ApiLevel < MinimumAgentLevel)
        {
            return ApiMatch.AgentTooOld;
        }

        return health.ApiLevel > Level ? ApiMatch.ClientBehind
            : health.ApiLevel < Level ? ApiMatch.AgentBehind
            : ApiMatch.Same;
    }
}

/// <summary>How a client and its agent stand to each other (<see cref="ApiCompatibility.Compare"/>).</summary>
public enum ApiMatch
{
    /// <summary>The agent does not say its level: too old to, and not judged.</summary>
    Unknown,

    /// <summary>Both speak the same level.</summary>
    Same,

    /// <summary>The agent is newer and still serves this client: it works, and a newer client is worth installing.</summary>
    ClientBehind,

    /// <summary>The agent no longer serves this client: it has to be updated before anything is done.</summary>
    ClientTooOld,

    /// <summary>The agent is older and this client still works with it: updating the agent is worth it.</summary>
    AgentBehind,

    /// <summary>This client needs a newer agent: the agent has to be updated on its machine.</summary>
    AgentTooOld,
}
