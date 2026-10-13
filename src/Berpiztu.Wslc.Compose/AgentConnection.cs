using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace Berpiztu.Wslc.Compose;

/// <summary>
/// The agent this run talks to. On its own machine it asks for nothing: the
/// agent lets in whoever calls it on the loopback. Another machine's is given
/// its API token, as any script is.
/// </summary>
internal sealed class AgentConnection : IDisposable
{
    public const string TokenVariable = "WSLC_COMPOSE_TOKEN";

    private readonly HttpClient _http;

    private AgentConnection(Uri address, HttpClient http, WslcAgentApi api) => (Address, _http, Api) = (address, http, api);

    public Uri Address { get; }

    public WslcAgentApi Api { get; }

    /// <summary>The level of the API the agent speaks, as it said when asked (<see cref="CheckAsync"/>); 0 when it did not.</summary>
    public int ApiLevel { get; private set; }

    /// <summary>The agent is on this machine: a path of this machine is a path of its own, and it reads the file itself.</summary>
    public bool IsLocal => Address.IsLoopback;

    public static AgentConnection Open(Arguments arguments)
    {
        var address = AgentAddress.Of(arguments.Agent);
        var token = new AgentAccessToken { Value = arguments.Token.Length > 0 ? arguments.Token : Environment.GetEnvironmentVariable(TokenVariable) };
        var http = new HttpClient(new AgentAccessHandler(token) { InnerHandler = new HttpClientHandler() })
        {
            BaseAddress = address,
            Timeout = WslcAgentApi.RequestTimeout,
        };
        return new AgentConnection(address, http, new WslcAgentApi(http, token));
    }

    /// <summary>
    /// The agent answers, and the two speak an API the other still serves.
    /// One of them too old for the other stops here, with which one to
    /// update, and not at the first call the older one does not know.
    /// </summary>
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (await Api.GetHealthAsync(cancellationToken) is not { } health)
        {
            return;
        }

        ApiLevel = health.ApiLevel;
        switch (ApiCompatibility.Compare(health))
        {
            case ApiMatch.ClientTooOld:
                throw new CommandFailed($"The agent at {Address} ({health.Version}) no longer serves this wslc-compose ({Program.Version}). Use the one installed with that agent.");
            case ApiMatch.AgentTooOld:
                throw new CommandFailed($"The agent at {Address} ({health.Version}) is older than this wslc-compose ({Program.Version}) works with. Update the agent.");
        }
    }

    /// <summary>
    /// Has the agent write in its log the warnings and the errors this
    /// command printed, as the pages do with what they show. In a few seconds
    /// at most, and with no complaint when it cannot be done: an agent that
    /// does not answer is what the command has just said, and it is not said
    /// twice.
    /// </summary>
    public async Task ReportAsync(IReadOnlyList<(string Level, string Text)> said, string where)
    {
        if (said.Count == 0)
        {
            return;
        }

        using var soon = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            foreach (var (level, text) in said)
            {
                await Api.ReportClientAsync(new ClientReport(level, text, where), soon.Token);
            }
        }
        catch (Exception)
        {
            // Not told: see above.
        }
    }

    public void Dispose() => _http.Dispose();
}
