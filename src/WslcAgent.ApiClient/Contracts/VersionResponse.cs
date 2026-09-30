namespace WslcAgent.ApiClient.Contracts;

/// <summary>Body of <c>GET /api/v1/version</c>.</summary>
/// <param name="Agent">Version of the running agent.</param>
/// <param name="Wslc">Version of the <c>wslc</c> CLI the agent drives, empty when it is not installed.</param>
public sealed record VersionResponse(string Agent, string Wslc);
