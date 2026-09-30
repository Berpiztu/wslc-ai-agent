namespace WslcAgent.ApiClient.Contracts;

/// <summary>Body of <c>GET /api/v1/dashboard/object-defaults</c>: how each kind of dashboard object is born, view by view.</summary>
/// <param name="Writable">This agent writes them back to its repository (a development build), so the board of every object can design them.</param>
/// <param name="Defaults">The client's own text, as it wrote it: each view's object types, each with its cells and its look. Empty when none ship.</param>
public sealed record ObjectDefaultsResponse(bool Writable, string Defaults);
