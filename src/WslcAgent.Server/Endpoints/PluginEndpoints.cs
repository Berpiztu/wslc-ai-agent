using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Plugins;

namespace WslcAgent.Server.Endpoints;

/// <summary>
/// <c>/api/v1/plugins</c>: Settings → Plugins, the plugins installed, switched
/// on and loaded, and what each wrote in the log. No MCP tool: what code the
/// agent runs is a person's decision. See docs/api-v1.md and docs/plugins.md.
/// </summary>
public static class PluginEndpoints
{
    public static RouteGroupBuilder MapPluginEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/plugins");

        group.MapGet("", (PluginService plugins) => plugins.List())
            .WithName("ListPlugins");

        group.MapPost("", (InstallPluginRequest request, PluginService plugins) => plugins.InstallAsync(request.Path ?? ""))
            .WithName("InstallPlugin");

        group.MapPut("/{name}", (string name, PluginSwitch request, PluginService plugins) => plugins.SwitchAsync(name, request.Enabled))
            .WithName("SwitchPlugin");

        group.MapDelete("/{name}", (string name, PluginService plugins) => plugins.RemoveAsync(name))
            .WithName("RemovePlugin");

        group.MapGet("/{name}/log", (string name, int? tail, PluginService plugins) => plugins.Log(name, tail))
            .WithName("PluginLog");

        return api;
    }
}
