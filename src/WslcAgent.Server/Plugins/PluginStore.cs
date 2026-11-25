using WslcAgent.Server.Host;

namespace WslcAgent.Server.Plugins;

/// <summary>One plugin as installed: what it is, where it came from, and whether it is switched on.</summary>
/// <param name="Name">Its assembly's name: its folder under <see cref="PluginStore.Root"/> is named by it.</param>
/// <param name="Assembly">The file loaded, in that folder.</param>
/// <param name="Version">Its assembly's version when it was installed.</param>
/// <param name="Description">What its assembly says it is.</param>
/// <param name="InstalledFrom">The file it was installed from.</param>
/// <param name="InstalledAt">When.</param>
/// <param name="Enabled">Switched on: loaded now and at every start of the agent.</param>
public sealed record PluginRecord(
    string Name,
    string Assembly,
    string Version,
    string Description,
    string InstalledFrom,
    DateTimeOffset InstalledAt,
    bool Enabled);

/// <summary>Body of <c>plugins.json</c>.</summary>
public sealed record PluginRecords(IReadOnlyList<PluginRecord> Plugins)
{
    /// <summary>None installed: what stands until the file exists.</summary>
    public static readonly PluginRecords None = new([]);
}

/// <summary>
/// The plugins installed, in <c>plugins.json</c> of the agent's data folder, and
/// their files, a folder each under <c>plugins</c> beside it. Only what is
/// written here is loaded: a folder put there by hand is not.
/// </summary>
public sealed class PluginStore(string dataDirectory) : SavedSettings<PluginRecords>(Path.Combine(dataDirectory, "plugins.json"), PluginRecords.None)
{
    /// <summary>Where every plugin's folder is.</summary>
    public string Root { get; } = Path.Combine(dataDirectory, "plugins");

    public string FolderOf(string name) => Path.Combine(Root, name);

    public PluginRecord? Find(string name) =>
        Get().Plugins.FirstOrDefault(plugin => plugin.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Puts the record in place of the one of the same name, or adds it.</summary>
    public void Put(PluginRecord record) =>
        Set(new PluginRecords([.. Get().Plugins.Where(plugin => !plugin.Name.Equals(record.Name, StringComparison.OrdinalIgnoreCase)), record]));

    public void Remove(string name) =>
        Set(new PluginRecords([.. Get().Plugins.Where(plugin => !plugin.Name.Equals(name, StringComparison.OrdinalIgnoreCase))]));
}
