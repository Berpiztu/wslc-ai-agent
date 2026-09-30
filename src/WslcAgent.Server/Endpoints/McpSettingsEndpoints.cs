using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Auth;
using WslcAgent.Server.Mcp;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/mcp/settings</c>: Settings → MCP server. See docs/api-v1.md.</summary>
public static class McpSettingsEndpoints
{
    public static RouteGroupBuilder MapMcpSettingsEndpoints(this RouteGroupBuilder api)
    {
        // address: the one the installer lines are written with. A client on
        // another machine cannot use the agent's own loopback, so the caller
        // says which address that machine reaches this agent by.
        api.MapGet("/mcp/settings", (McpSettingsStore store, IServer server, IOptions<McpServerOptions> mcp, AgentLogin login, SkillFile skill, SkillClients clients, IOptions<WslcOptions> wslc, string? address) =>
                Status(store.Get(), server, mcp, login, skill, clients, wslc, address))
            .WithName("GetMcpSettings");

        api.MapPut("/mcp/settings", (McpSettings settings, McpSettingsStore store, IServer server, IOptions<McpServerOptions> mcp, AgentLogin login, SkillFile skill, SkillClients clients, IOptions<WslcOptions> wslc, string? address) =>
                Status(store.Set(settings), server, mcp, login, skill, clients, wslc, address))
            .WithName("SetMcpSettings");

        // The skill itself: what a client's own installer reads from a URL,
        // what the browser saves, and what Install writes into a folder.
        // Hermes takes the URL as it is, so it also answers under the file name
        // it expects to find at the end of one.
        api.MapGet("/mcp/skill", (SkillFile skill) => SkillResult(skill))
            .WithName("GetMcpSkill");

        api.MapGet("/mcp/skill/SKILL.md", (SkillFile skill) => SkillResult(skill))
            .WithName("GetMcpSkillFile");

        // Each client's own installer, run here: the agent knows the line and
        // the address, and the operator reads what the installer answered.
        // What a machine has, asked of that machine: this one, or the far end
        // of an ssh hop. Without it the page could only offer what the agent's
        // own machine holds, which is not where the client always is.
        api.MapGet("/mcp/skill/clients", (SkillClients clients, string? ssh, CancellationToken cancellationToken) =>
                (ssh ?? "").Trim() is { Length: > 0 } destination
                    ? clients.RemoteAsync(destination, cancellationToken)
                    : Task.FromResult(clients.Local()))
            .WithName("GetSkillClients");

        api.MapPost("/mcp/skill/install", (SkillInstallRequest request, SkillInstaller installer, IServer server, CancellationToken cancellationToken) =>
                installer.InstallAsync(
                    request.Client,
                    $"{Address(request.Address, server)}/api/v1/mcp/skill/{SkillFile.FileName}",
                    request.Profile,
                    request.SshDestination,
                    cancellationToken))
            .WithName("InstallMcpSkill");

        return api;
    }

    private static IResult SkillResult(SkillFile skill) =>
        Results.File(System.Text.Encoding.UTF8.GetBytes(skill.Text), "text/markdown; charset=utf-8", SkillFile.FileName);

    /// <summary>The switches plus what a client would find with them: the URL to register, how many tools it would see, and how to install the skill.</summary>
    private static McpStatus Status(McpSettings settings, IServer server, IOptions<McpServerOptions> mcp, AgentLogin login, SkillFile skill, SkillClients clients, IOptions<WslcOptions> wslc, string? address)
    {
        var tools = mcp.Value.ToolCollection ?? [];
        var destructive = tools.Count(tool => tool.ProtocolTool.Annotations?.DestructiveHint is true);
        var visible = settings switch
        {
            { Enabled: false } => 0,
            { AllowDestructiveTools: false } => tools.Count - destructive,
            _ => tools.Count,
        };

        var agent = LocalUrl(server);
        var chosen = Address(address, server);
        var skillUrl = $"{chosen}/api/v1/mcp/skill/{SkillFile.FileName}";
        return new McpStatus(settings, agent, visible, destructive, login.HasApiToken, skill.Targets(),
            SkillFile.Commands(skillUrl, OperatingSystem.IsWindows()), chosen, clients.Hosts());
    }

    /// <summary>The address the client's machine reaches this agent by: what it asked for, or this agent's own.</summary>
    private static string Address(string? asked, IServer server) =>
        (asked ?? "").Trim().TrimEnd('/') is { Length: > 0 } given ? given : LocalUrl(server)[..^McpPath.Length];

/// <summary>The path a client registers: versioned with the API, because the tool surface is what a client depends on.</summary>
    public const string McpPath = "/api/v1/mcp";

    /// <summary>The address to paste into a client, taken from what the agent is actually listening on.</summary>
    private static string LocalUrl(IServer server)
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var address = addresses.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) ?? addresses.FirstOrDefault();
        return address is null
            ? $"http://127.0.0.1:{InstalledAgentSettings.DefaultPort}{McpPath}"
            : $"{address.Replace("[::]", "127.0.0.1").Replace("*", "127.0.0.1").TrimEnd('/')}{McpPath}";
    }
}
