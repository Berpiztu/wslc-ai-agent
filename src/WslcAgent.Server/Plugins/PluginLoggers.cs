namespace WslcAgent.Server.Plugins;

/// <summary>
/// The agent's logging as a plugin is given it: every logger it makes writes
/// in the agent's log under <c>Plugin NAME/its own name</c>
/// (<c>Plugin IaExtract.EngineFacts/ProviderFacts</c>), the plugin's name
/// taken off the front of a type's, so the Logs page's Plugins finds all of it
/// and its source says which plugin wrote it.
/// </summary>
internal sealed class PluginLoggers(ILoggerFactory agent, string plugin) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => agent.CreateLogger(CategoryOf(categoryName));

    /// <summary>The agent's providers are the agent's: a plugin adds none.</summary>
    public void AddProvider(ILoggerProvider provider)
    {
    }

    /// <summary>The agent's logging outlives the plugin: nothing of it is disposed here.</summary>
    public void Dispose()
    {
    }

    private string CategoryOf(string category)
    {
        var own = category.StartsWith(plugin + ".", StringComparison.Ordinal) ? category[(plugin.Length + 1)..] : category;
        return $"{PluginHost.LogPrefix}{plugin}/{own}";
    }
}
