namespace WslcAgent.ApiClient.Contracts;

/// <summary>Body of <c>GET /api/v1/images/inspect?reference=</c>: the raw <c>wslc image inspect</c> JSON, indented.</summary>
public sealed record ImageInspect(string Reference, string Json);
