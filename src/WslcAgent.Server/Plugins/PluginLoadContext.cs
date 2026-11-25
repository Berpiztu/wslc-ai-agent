using System.Reflection;
using System.Runtime.Loader;

namespace WslcAgent.Server.Plugins;

/// <summary>
/// Where a plugin's assemblies are loaded: its own, and the libraries it brings
/// in its folder (its <c>.deps.json</c> says which). Whatever the agent runs on
/// already, .NET and ASP.NET Core above all, is the agent's own: a plugin
/// speaks to the agent through those types, and a second copy of them would be
/// other types. Each file is read into memory and loaded from there, so the
/// plugin's folder is never held open: it can be installed again or removed
/// while the agent runs, and the change is loaded at its next start.
/// </summary>
internal sealed class PluginLoadContext(string assemblyPath, bool isCollectible = false)
    : AssemblyLoadContext($"plugin {Path.GetFileNameWithoutExtension(assemblyPath)}", isCollectible)
{
    /// <summary>The names of the assemblies the agent runs on: the framework's, and the agent's own.</summary>
    private static readonly Lazy<HashSet<string>> Platform = new(() =>
        ((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase));

    private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);

    /// <summary>The plugin's own assembly.</summary>
    public Assembly LoadPlugin() => FromBytes(assemblyPath);

    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name is { } simple && (Platform.Value.Contains(simple) || Default.Assemblies.Any(loaded => simple.Equals(loaded.GetName().Name, StringComparison.OrdinalIgnoreCase))))
        {
            return null;
        }

        return _resolver.ResolveAssemblyToPath(name) is { } path ? FromBytes(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
        _resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;

    private Assembly FromBytes(string path)
    {
        using var assembly = new MemoryStream(File.ReadAllBytes(path));
        var symbols = Path.ChangeExtension(path, ".pdb");
        if (!File.Exists(symbols))
        {
            return LoadFromStream(assembly);
        }

        using var pdb = new MemoryStream(File.ReadAllBytes(symbols));
        return LoadFromStream(assembly, pdb);
    }
}
