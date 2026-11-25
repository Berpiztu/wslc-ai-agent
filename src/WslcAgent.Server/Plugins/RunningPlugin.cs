namespace WslcAgent.Server.Plugins;

/// <summary>
/// A plugin loaded and running: its own services and the pipeline its startup
/// filters made, which the agent runs behind its login (<see cref="PluginsMiddleware"/>).
/// While a request is in the plugin's pipeline its services are the plugin's,
/// with the agent's behind them; when the plugin lets it go on, the agent's
/// again.
/// </summary>
internal sealed class RunningPlugin : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly PluginLoadContext _context;
    private readonly RequestDelegate _pipeline;
    private readonly IReadOnlyList<IHostedService> _hosted;
    private readonly ILogger _log;

    /// <summary>Where a request in its pipeline keeps the way on, for the pipeline's end to take it.</summary>
    private readonly object _onwardKey = new();

    public RunningPlugin(PluginAssembly assembly, ServiceProvider services, PluginLoadContext context, IReadOnlyList<IHostedService> hosted, ILogger log)
    {
        Assembly = assembly;
        _log = log;
        _services = services;
        _context = context;
        _hosted = hosted;

        // As ASP.NET Core composes a host's startup filters, the last registered
        // innermost; at the end, the request goes back to the agent.
        Action<IApplicationBuilder> configure = app => app.Run(http => ((Func<Task>)http.Items[_onwardKey]!)());
        foreach (var filter in services.GetServices<IStartupFilter>().Reverse())
        {
            configure = filter.Configure(configure);
        }

        var builder = new ApplicationBuilder(services);
        configure(builder);
        _pipeline = builder.Build();
    }

    public PluginAssembly Assembly { get; }

    /// <summary>
    /// The request through the plugin's pipeline. One the plugin answers itself,
    /// not letting it go on, is written in its log: what was asked, the answer,
    /// how long it took and who asked (<see cref="CallerOf"/>).
    /// </summary>
    public async Task InvokeAsync(HttpContext http, Func<Task> onward)
    {
        var agents = http.RequestServices;
        var wentOn = false;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        await using var scope = _services.CreateAsyncScope();
        var own = new PluginRequestServices(scope.ServiceProvider, agents);
        http.RequestServices = own;
        http.Items[_onwardKey] = async () =>
        {
            wentOn = true;
            http.RequestServices = agents;
            try
            {
                await onward();
            }
            finally
            {
                http.RequestServices = own;
            }
        };

        try
        {
            await _pipeline(http);
        }
        finally
        {
            http.Items.Remove(_onwardKey);
            http.RequestServices = agents;
            if (!wentOn)
            {
                _log.LogInformation("{Method} {Path} answered {Status} in {Milliseconds} ms to {Caller}", http.Request.Method, http.Request.Path.Value,
                    http.Response.StatusCode, (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, CallerOf(http));
            }
        }
    }

    /// <summary>The header a caller names itself with: a container sends its own name, which its address does not tell.</summary>
    public const string CallerHeader = "X-Caller";

    /// <summary>
    /// Who asked, as far as the request says it: the name the caller gives in
    /// <see cref="CallerHeader"/> (a container's own), its address, and its
    /// program (User-Agent). A container's request may arrive from the loopback,
    /// as this machine's own, so its address alone does not say which.
    /// </summary>
    private static string CallerOf(HttpContext http)
    {
        var named = http.Request.Headers[CallerHeader].ToString().Trim();
        var address = http.Connection.RemoteIpAddress is { } ip ? (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString() : "an unknown address";
        var program = http.Request.Headers.UserAgent.ToString().Trim();
        return $"{(named.Length > 0 ? named : "a caller that gave no name")} from {address}" + (program.Length > 0 ? $" ({program})" : "");
    }

    /// <summary>Its background services stopped, its services disposed and its assemblies let go.</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var service in _hosted.Reverse())
        {
            await service.StopAsync(CancellationToken.None);
        }

        await _services.DisposeAsync();
        _context.Unload();
    }

    /// <summary>The plugin's services first; what it does not have, the agent's (its logging, its configuration…).</summary>
    private sealed class PluginRequestServices(IServiceProvider own, IServiceProvider agents) : IServiceProvider
    {
        public object? GetService(Type serviceType) => own.GetService(serviceType) ?? agents.GetService(serviceType);
    }
}
