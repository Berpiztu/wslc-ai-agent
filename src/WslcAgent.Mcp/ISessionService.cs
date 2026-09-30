using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>WSLC session operations the MCP tools need from the host. Implemented by the server.</summary>
public interface ISessionService
{
    /// <summary>Running sessions plus the one the agent targets.</summary>
    Task<SessionsResponse> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Make every following command target <paramref name="name"/> — empty for
    /// the CLI's own store for this user — and answer with the session now
    /// targeted, which always has a name.
    /// </summary>
    string Select(string name);

    /// <summary>Starts a session (empty: the one the agent targets), and says what it did.</summary>
    Task<SessionActionResult> StartAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Stops a session (empty: the one the agent targets), taking down what runs inside it.</summary>
    Task<SessionActionResult> StopAsync(string name, CancellationToken cancellationToken = default);
}
