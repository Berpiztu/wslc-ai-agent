namespace WslcAgent.Server.Plugins;

/// <summary>
/// The web host a plugin's hosting startup is given: what it adds to its
/// services goes into the plugin's own collection, built apart from the
/// agent's so the plugin can be loaded and unloaded while the agent runs. It
/// reads the agent's configuration and environment; it cannot build a host.
/// </summary>
internal sealed class PluginWebHostBuilder(IServiceCollection services, WebHostBuilderContext context) : IWebHostBuilder
{
    private readonly Dictionary<string, string?> _settings = new(StringComparer.OrdinalIgnoreCase);

    public IWebHost Build() => throw new NotSupportedException("A plugin is loaded into the WSLC AI Agent's host; it does not build one.");

    /// <summary>A plugin reads the agent's configuration; it does not add to it.</summary>
    public IWebHostBuilder ConfigureAppConfiguration(Action<WebHostBuilderContext, IConfigurationBuilder> configureDelegate) => this;

    public IWebHostBuilder ConfigureServices(Action<IServiceCollection> configureServices)
    {
        configureServices(services);
        return this;
    }

    public IWebHostBuilder ConfigureServices(Action<WebHostBuilderContext, IServiceCollection> configureServices)
    {
        configureServices(context, services);
        return this;
    }

    public string? GetSetting(string key) => _settings.TryGetValue(key, out var value) ? value : context.Configuration[key];

    public IWebHostBuilder UseSetting(string key, string? value)
    {
        _settings[key] = value;
        return this;
    }
}
