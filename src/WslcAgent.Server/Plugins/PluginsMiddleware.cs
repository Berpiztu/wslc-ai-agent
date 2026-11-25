namespace WslcAgent.Server.Plugins;

/// <summary>
/// Every running plugin's pipeline, one after the other, behind the agent's
/// login: what a plugin answers it answers, and what it lets go on goes to the
/// next one and then to the agent. The plugins are taken as they are when the
/// request comes, so one loaded or unloaded meanwhile changes only the next.
/// </summary>
internal sealed class PluginsMiddleware(RequestDelegate next, PluginHost host)
{
    public Task InvokeAsync(HttpContext http) => RunAsync(http, host.Running, 0);

    private Task RunAsync(HttpContext http, IReadOnlyList<RunningPlugin> plugins, int index) =>
        index == plugins.Count ? next(http) : plugins[index].InvokeAsync(http, () => RunAsync(http, plugins, index + 1));
}
