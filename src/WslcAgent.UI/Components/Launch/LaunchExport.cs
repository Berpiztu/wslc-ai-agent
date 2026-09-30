using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components.Launch;

/// <summary>
/// What an export of the form carries. Off, the export is what <c>wslc</c> itself
/// knows and takes back; on ("Full variables, WSLC AI Agent compatible"), also
/// what only the agent keeps — the restart policy and the public names — for
/// another agent. Load JSON reads both shapes.
/// </summary>
public static class LaunchExport
{
    public static ContainerLaunchRequest Of(ContainerLaunchRequest request, bool fullVariables) =>
        fullVariables ? request : request with { RestartPolicy = RestartPolicyInfo.No, PublicNames = [] };
}
