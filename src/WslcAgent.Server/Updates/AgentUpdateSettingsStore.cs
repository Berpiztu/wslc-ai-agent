using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Host;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Updates;

/// <summary>
/// Settings → Agent update, kept in <c>agent-update.json</c> in the agent's data
/// folder. Until the file exists the agent follows <c>Updates:AutoUpdate</c>,
/// which ships on: the point of a self-updating agent is that copying its
/// installer next to it is the whole deployment.
/// </summary>
public sealed class AgentUpdateSettingsStore(IOptions<WslcOptions> options, IConfiguration configuration)
    : SavedSettings<AgentUpdateSettings>(
        Path.Combine(options.Value.DataDirectory, "agent-update.json"),
        new AgentUpdateSettings(configuration.GetValue("Updates:AutoUpdate", true)));
