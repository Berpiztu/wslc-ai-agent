using System.Reflection;

namespace WslcAgent.Server.Plugins;

/// <summary>What a plugin's assembly says of itself: its name, its version, what it is and its hosting startups.</summary>
internal sealed record PluginAssembly(string Name, string Version, string Description, IReadOnlyList<Type> Startups)
{
    public static PluginAssembly Of(Assembly assembly)
    {
        var name = assembly.GetName().Name ?? throw new ArgumentException("The assembly has no name.");
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? assembly.GetName().Version?.ToString()
            ?? "";
        var description = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description is { Length: > 0 } said ? said
            : assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? "";
        var startups = assembly.GetCustomAttributes<HostingStartupAttribute>().Select(startup => startup.HostingStartupType).ToList();
        return new PluginAssembly(name, version, description, startups);
    }
}
