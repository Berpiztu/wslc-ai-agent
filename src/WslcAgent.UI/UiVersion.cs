using System.Reflection;

namespace WslcAgent.UI;

/// <summary>The UI assembly's version, as Directory.Build.props sets it: what a static asset of this assembly is stamped with.</summary>
public static class UiVersion
{
    public static readonly string Current =
        typeof(UiVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(UiVersion).Assembly.GetName().Version?.ToString()
        ?? "0";
}
