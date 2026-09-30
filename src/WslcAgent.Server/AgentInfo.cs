using System.Reflection;
using WslcAgent.Mcp;

namespace WslcAgent.Server;

/// <summary>Version of the running agent, read from the assembly metadata.</summary>
public sealed class AgentInfo : IAgentInfo
{
    public string Version { get; } =
        typeof(AgentInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AgentInfo).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <summary>
    /// The module's own id, which the compiler makes anew for every build.
    /// The version cannot do this job: it is written by hand and stays where
    /// it is while the code under it changes all morning.
    /// </summary>
    public string Build { get; } = typeof(AgentInfo).Assembly.ManifestModule.ModuleVersionId.ToString("N");
}
