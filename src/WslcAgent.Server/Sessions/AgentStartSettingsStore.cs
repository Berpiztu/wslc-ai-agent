using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Host;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Sessions;

/// <summary>Settings → Security → Session at agent start, kept in <c>agent-start.json</c>; the session is started until changed.</summary>
public sealed class AgentStartSettingsStore(IOptions<WslcOptions> options)
    : SavedSettings<AgentStartSettings>(Path.Combine(options.Value.DataDirectory, "agent-start.json"), new AgentStartSettings());