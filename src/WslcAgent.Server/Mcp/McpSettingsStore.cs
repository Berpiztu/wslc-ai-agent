using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Host;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Mcp;

/// <summary>
/// Settings → MCP server, kept in <c>mcp.json</c> in the agent's data folder. The
/// file is what the running agent obeys, and it is read from memory on every
/// call: the operator switches the destructive tools off and the next request
/// already sees them gone, with no restart. Until the file exists the agent
/// follows <c>appsettings.json</c> (<c>Mcp:Enabled</c>,
/// <c>Mcp:AllowDestructiveTools</c>, which ships <c>false</c>).
/// </summary>
public sealed class McpSettingsStore(IOptions<WslcOptions> options, IConfiguration configuration)
    : SavedSettings<McpSettings>(
        Path.Combine(options.Value.DataDirectory, "mcp.json"),
        new McpSettings(configuration.GetValue("Mcp:Enabled", true), configuration.GetValue("Mcp:AllowDestructiveTools", false)));
