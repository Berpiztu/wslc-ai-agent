namespace WslcAgent.ApiClient.Contracts;

/// <summary>
/// A plugin of the agent's: an ASP.NET Core hosting startup (<c>IHostingStartup</c>,
/// named by <c>[assembly: HostingStartup]</c>) that the agent loads while it runs,
/// the moment it is installed and switched on in Settings → Plugins, and unloads
/// when it is switched off or removed (docs/plugins.md).
/// </summary>
/// <param name="Name">Its assembly's name, which names it everywhere: its folder, its record, its log.</param>
/// <param name="Version">The version its assembly says, as it was installed.</param>
/// <param name="Description">What its assembly says it is (its description, else its title); empty when it says nothing.</param>
/// <param name="Assembly">The file the agent loads, in the plugin's folder.</param>
/// <param name="Folder">The plugin's folder, in the agent's data folder: its files as they were copied at the install.</param>
/// <param name="InstalledFrom">The file it was installed from.</param>
/// <param name="InstalledAt">When, <c>yyyy-MM-dd HH:mm</c> local time.</param>
/// <param name="Enabled">Switched on: loaded now and at every start of the agent.</param>
/// <param name="Loaded">Loaded in the agent that is running now.</param>
/// <param name="LoadedVersion">The version loaded now; empty when it is not loaded.</param>
/// <param name="Error">Why it could not be loaded; empty when it was, or is switched off.</param>
public sealed record PluginInfo(
    string Name,
    string Version,
    string Description,
    string Assembly,
    string Folder,
    string InstalledFrom,
    string InstalledAt,
    bool Enabled,
    bool Loaded,
    string LoadedVersion,
    string Error);

/// <summary>Body of <c>GET /api/v1/plugins</c>: every plugin installed.</summary>
/// <param name="Plugins">By name.</param>
/// <param name="Folder">Where the plugins' folders are, in the agent's data folder.</param>
public sealed record PluginList(IReadOnlyList<PluginInfo> Plugins, string Folder);

/// <summary>Body of <c>POST /api/v1/plugins</c>: the plugin's assembly, a file of the agent's machine.</summary>
public sealed record InstallPluginRequest(string Path);

/// <summary>Body of <c>PUT /api/v1/plugins/{name}</c>.</summary>
public sealed record PluginSwitch(bool Enabled);
