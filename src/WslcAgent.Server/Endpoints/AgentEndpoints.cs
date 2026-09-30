using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Endpoints;

/// <summary>Endpoints about the agent itself: health and versions. See docs/api-v1.md.</summary>
public static class AgentEndpoints
{
    public static RouteGroupBuilder MapAgentEndpoints(this RouteGroupBuilder api)
    {
        // The build is what a client watches to know the agent it was served by
        // has been replaced: a rebuild changes it, the version does not.
        api.MapGet("/health", (IAgentInfo info, IHostEnvironment host) =>
                new HealthResponse("ok", info.Version, info.Build, host.IsDevelopment()))
            .WithName("Health");

        api.MapGet("/version", GetVersionAsync)
            .WithName("Version");

        return api;
    }

    /// <summary>Agent and CLI versions; a missing CLI is reported as an empty string, not an error.</summary>
    private static async Task<VersionResponse> GetVersionAsync(IAgentInfo info, IWslcRunner wslc, CancellationToken ct)
    {
        try
        {
            var result = await wslc.RunAsync(["version", "--format", "json"], cancellationToken: ct);
            var row = WslcJson.ParseRows(result.Stdout).FirstOrDefault();
            var wslcVersion = row.ValueKind == System.Text.Json.JsonValueKind.Object && row.TryGetProperty("Client", out var client)
                ? client.GetString("Version")
                : "";
            return new VersionResponse(info.Version, wslcVersion);
        }
        catch (WslcNotFoundException)
        {
            return new VersionResponse(info.Version, "");
        }
    }
}
