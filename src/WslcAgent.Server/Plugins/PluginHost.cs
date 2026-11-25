namespace WslcAgent.Server.Plugins;

/// <summary>A plugin as the agent has it now: loaded, or why not.</summary>
/// <param name="Name">Its name, as installed.</param>
/// <param name="Version">The version loaded; empty when it is not.</param>
/// <param name="Error">Why it could not be loaded; empty when it was, or was not tried.</param>
public sealed record PluginState(string Name, string Version, string Error)
{
    public bool Loaded => Version.Length > 0 && Error.Length == 0;
}

/// <summary>
/// The plugins running in the agent, loaded and unloaded while it runs: at its
/// start every one switched on, then each time one is installed, switched or
/// removed. A plugin is a hosting startup (<c>[assembly: HostingStartup]</c>)
/// given a web host of its own (<see cref="PluginWebHostBuilder"/>): its
/// services are built apart from the agent's, with the agent's logging,
/// configuration and environment in them, and its startup filters make a
/// pipeline the agent runs behind its login. Its assemblies are in a load
/// context of their own that is let go when it is unloaded. One that cannot
/// be loaded is said in Settings → Plugins and in the log, and nothing else
/// stops.
/// </summary>
public sealed class PluginHost(IServiceProvider agent, ILoggerFactory loggers)
{
    /// <summary>What the agent writes about a plugin, in its log: <c>Plugin NAME</c>.</summary>
    public const string LogPrefix = "Plugin ";

    /// <summary>A request already in a plugin's pipeline when it is unloaded is given this long to end before its services go.</summary>
    private static readonly TimeSpan Draining = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, PluginState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RunningPlugin> _running = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The plugins running, in the order they were loaded: what a request goes through now.</summary>
    internal IReadOnlyList<RunningPlugin> Running { get; private set; } = [];

    public PluginState? Find(string name)
    {
        lock (_states)
        {
            return _states.GetValueOrDefault(name);
        }
    }

    public IReadOnlyList<PluginState> All()
    {
        lock (_states)
        {
            return [.. _states.Values];
        }
    }

    /// <summary>Loads the plugin from its folder, in place of the one of that name running now.</summary>
    public async Task<PluginState> LoadAsync(PluginRecord record, string folder)
    {
        await _gate.WaitAsync();
        try
        {
            Stop(record.Name);
            var state = Start(record, folder);
            Log(record.Name, state);
            return state;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Unloads the plugin of that name, when it runs; its state goes.</summary>
    public async Task UnloadAsync(string name)
    {
        await _gate.WaitAsync();
        try
        {
            if (Stop(name))
            {
                loggers.CreateLogger(LogPrefix + name).LogWarning("Unloaded");
            }

            lock (_states)
            {
                _states.Remove(name);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private PluginState Start(PluginRecord record, string folder)
    {
        var path = Path.Combine(folder, record.Assembly);
        if (!File.Exists(path))
        {
            return Keep(new PluginState(record.Name, "", $"Its file is not there: {path}"));
        }

        var context = new PluginLoadContext(path, isCollectible: true);
        ServiceProvider? services = null;
        try
        {
            var assembly = PluginAssembly.Of(context.LoadPlugin());
            if (assembly.Startups.Count == 0)
            {
                throw new InvalidOperationException("Its assembly names no hosting startup ([assembly: HostingStartup]).");
            }

            services = Services(assembly, record.Name);
            var hosted = services.GetServices<IHostedService>().ToList();
            foreach (var service in hosted)
            {
                service.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            }

            var running = new RunningPlugin(assembly, services, context, hosted, loggers.CreateLogger(LogPrefix + record.Name));
            _running[record.Name] = running;
            Running = [.. Running, running];
            return Keep(new PluginState(record.Name, assembly.Version, ""));
        }
        catch (Exception exception)
        {
            services?.Dispose();
            context.Unload();
            return Keep(new PluginState(record.Name, "", Said(exception)));
        }
    }

    /// <summary>The plugin's own services: what its hosting startups add, over the agent's logging (under its name), configuration and environment.</summary>
    private ServiceProvider Services(PluginAssembly assembly, string name)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(new PluginLoggers(loggers, name));
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton(agent.GetRequiredService<IConfiguration>());
        services.AddSingleton(agent.GetRequiredService<IWebHostEnvironment>());
        services.AddSingleton<IHostEnvironment>(agent.GetRequiredService<IWebHostEnvironment>());
        services.AddSingleton(agent.GetRequiredService<IHostApplicationLifetime>());
        services.AddOptions();
        var context = new WebHostBuilderContext
        {
            Configuration = agent.GetRequiredService<IConfiguration>(),
            HostingEnvironment = agent.GetRequiredService<IWebHostEnvironment>(),
        };
        var host = new PluginWebHostBuilder(services, context);
        foreach (var type in assembly.Startups)
        {
            var startup = Activator.CreateInstance(type) as IHostingStartup
                ?? throw new InvalidOperationException($"{type.FullName} is not an IHostingStartup.");
            startup.Configure(host);
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /// <summary>Takes the plugin off the requests; its services go once what was in its pipeline has had time to end.</summary>
    private bool Stop(string name)
    {
        if (!_running.Remove(name, out var running))
        {
            return false;
        }

        Running = [.. Running.Where(plugin => plugin != running)];
        _ = Task.Delay(Draining).ContinueWith(async _ =>
        {
            try
            {
                await running.DisposeAsync();
            }
            catch (Exception exception)
            {
                loggers.CreateLogger(LogPrefix + name).LogWarning("Not unloaded cleanly: {Error}", Said(exception));
            }
        }, TaskScheduler.Default);
        return true;
    }

    private PluginState Keep(PluginState state)
    {
        lock (_states)
        {
            _states[state.Name] = state;
        }

        return state;
    }

    private void Log(string name, PluginState state)
    {
        var logger = loggers.CreateLogger(LogPrefix + name);
        if (state.Loaded)
        {
            logger.LogInformation("Loaded {Name} {Version}", name, state.Version);
        }
        else
        {
            logger.LogError("Not loaded: {Error}", state.Error);
        }
    }

    /// <summary>The error and what caused it, in one line: a load that fails often says why only in its inner exception.</summary>
    private static string Said(Exception exception)
    {
        var said = new List<string>();
        for (Exception? error = exception; error is not null; error = error.InnerException)
        {
            said.Add(error.Message);
        }

        return string.Join(" ← ", said.Distinct());
    }
}
