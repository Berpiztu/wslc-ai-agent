using System.Globalization;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.AgentLog;

namespace WslcAgent.Server.Plugins;

/// <summary>
/// Settings → Plugins: what is installed, switched on and loaded; installing,
/// switching and removing, each at once, with no new start of the agent
/// (<see cref="PluginHost"/>); and what each plugin wrote in the agent's log.
/// </summary>
public sealed class PluginService(PluginStore store, PluginHost host, AgentLogs logs, ILoggerFactory loggers)
{
    /// <summary>A plugin's folder is copied whole at the install: this much at most, so a file picked in the wrong folder copies no drive.</summary>
    private const long LargestPlugin = 512L * 1024 * 1024;

    /// <summary>The folder a plugin's native libraries are in (<c>runtimes/win-x64/native/…</c>), copied with the files beside the assembly.</summary>
    private const string RuntimesFolder = "runtimes";

    /// <summary>At the agent's start: every plugin switched on, loaded.</summary>
    public async Task LoadSwitchedOnAsync()
    {
        foreach (var record in store.Get().Plugins.Where(plugin => plugin.Enabled))
        {
            await host.LoadAsync(record, store.FolderOf(record.Name));
        }
    }

    public PluginList List() =>
        new([.. store.Get().Plugins.Select(Info).OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)], store.Root);

    /// <summary>
    /// Installs the plugin whose assembly is <paramref name="path"/>: the files
    /// beside it and its <c>runtimes</c> folder are copied into a folder of its
    /// own, in place of the one of the same name, and it is switched on and
    /// loaded, in place of the one running. An assembly with no hosting startup
    /// is refused before anything is copied.
    /// </summary>
    public async Task<PluginList> InstallAsync(string path)
    {
        var source = Path.GetFullPath(path.Trim().Trim('"'));
        if (!source.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
        {
            throw new ArgumentException($"No assembly at {source}: a plugin is a .dll file of the agent's machine.");
        }

        var folder = Path.GetDirectoryName(source)!;
        if (IsUnder(folder, store.Root))
        {
            throw new ArgumentException($"{source} is in the agent's own plugin folder: install it from the folder it was built or published to.");
        }

        var plugin = Inspect(source);
        var files = FilesOf(folder);
        if (files.Sum(file => file.Length) > LargestPlugin)
        {
            throw new ArgumentException($"{folder} holds more than {LargestPlugin / (1024 * 1024)} MB: publish the plugin to a folder of its own and install it from there.");
        }

        var target = store.FolderOf(plugin.Name);
        try
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            foreach (var file in files)
            {
                var copy = Path.Combine(target, Path.GetRelativePath(folder, file.FullName));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                file.CopyTo(copy, overwrite: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"The plugin's files could not be copied to {target}: {exception.Message}", exception);
        }

        var record = new PluginRecord(plugin.Name, Path.GetFileName(source), plugin.Version, plugin.Description, source, DateTimeOffset.Now, Enabled: true);
        store.Put(record);
        LoggerOf(plugin.Name).LogWarning("Installed {Version} from {Source}, switched on", plugin.Version, source);
        await host.LoadAsync(record, target);
        return List();
    }

    public async Task<PluginList> SwitchAsync(string name, bool enabled)
    {
        var record = store.Find(name) ?? throw new KeyNotFoundException($"No plugin {name} is installed.");
        store.Put(record with { Enabled = enabled });
        LoggerOf(record.Name).LogWarning("Switched {State}", enabled ? "on" : "off");
        if (enabled)
        {
            await host.LoadAsync(record, store.FolderOf(record.Name));
        }
        else
        {
            await host.UnloadAsync(record.Name);
        }

        return List();
    }

    /// <summary>Unloads it, and takes its record and its folder away.</summary>
    public async Task<PluginList> RemoveAsync(string name)
    {
        var record = store.Find(name) ?? throw new KeyNotFoundException($"No plugin {name} is installed.");
        await host.UnloadAsync(record.Name);
        store.Remove(record.Name);
        try
        {
            var folder = store.FolderOf(record.Name);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The record is gone, so it is never loaded again; its files are only left behind.
            LoggerOf(record.Name).LogWarning("Removed, but its folder could not be deleted: {Error}", exception.Message);
        }

        LoggerOf(record.Name).LogWarning("Removed");
        return List();
    }

    /// <summary>
    /// What the plugin wrote in the agent's log, the newest last: its own lines
    /// (<c>Plugin NAME/…</c>, <see cref="PluginLoggers"/>), those of each request
    /// it answered, and the agent's about it (<c>Plugin NAME</c>: installed,
    /// switched, loaded, not loaded and why, unloaded, removed).
    /// </summary>
    public IReadOnlyList<AgentLogEntry> Log(string name, int? tail)
    {
        var own = PluginHost.LogPrefix + name;
        var entries = logs.Read(AgentLogs.MaxTail)
            .Where(entry => entry.Source.Equals(own, StringComparison.OrdinalIgnoreCase) || entry.Source.StartsWith(own + "/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var kept = Math.Clamp(tail ?? AgentLogs.DefaultTail, 1, AgentLogs.MaxTail);
        return entries.Count > kept ? entries[^kept..] : entries;
    }

    private PluginInfo Info(PluginRecord record)
    {
        var now = host.Find(record.Name);
        var running = now is { Loaded: true };
        return new PluginInfo(
            record.Name, record.Version, record.Description, record.Assembly, store.FolderOf(record.Name), record.InstalledFrom,
            record.InstalledAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            record.Enabled, running, running ? now!.Version : "", record.Enabled ? now?.Error ?? "" : "");
    }

    /// <summary>The plugin's name, version and what it is, read in a context of its own that is let go afterwards.</summary>
    private static PluginAssembly Inspect(string path)
    {
        var context = new PluginLoadContext(path, isCollectible: true);
        try
        {
            var plugin = PluginAssembly.Of(context.LoadPlugin());
            if (plugin.Startups.Count == 0)
            {
                throw new ArgumentException($"{Path.GetFileName(path)} is not a plugin: its assembly names no hosting startup ([assembly: HostingStartup]).");
            }

            return plugin;
        }
        catch (BadImageFormatException exception)
        {
            throw new ArgumentException($"{Path.GetFileName(path)} is not a .NET assembly: {exception.Message}", exception);
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>The files beside the assembly, and its <c>runtimes</c> folder whole.</summary>
    private static List<FileInfo> FilesOf(string folder)
    {
        var directory = new DirectoryInfo(folder);
        var files = directory.GetFiles().ToList();
        var runtimes = new DirectoryInfo(Path.Combine(folder, RuntimesFolder));
        if (runtimes.Exists)
        {
            files.AddRange(runtimes.GetFiles("*", SearchOption.AllDirectories));
        }

        return files;
    }

    private static bool IsUnder(string folder, string root) =>
        (Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private ILogger LoggerOf(string name) => loggers.CreateLogger(PluginHost.LogPrefix + name);
}
