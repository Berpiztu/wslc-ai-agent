namespace WslcAgent.ApiClient.Contracts;

/// <summary>Body of <c>POST /api/v1/registry/login</c>. Every field is optional, as <c>wslc login</c> takes them.</summary>
/// <param name="Server">The registry; empty for the session's default.</param>
/// <param name="Password">Password or personal access token; handed to the CLI on its standard input, never in its arguments.</param>
public sealed record RegistryLoginRequest(string Server = "", string Username = "", string Password = "");

/// <summary>Body of <c>POST /api/v1/registry/logout</c>.</summary>
/// <param name="Server">The registry; empty for the session's default.</param>
public sealed record RegistryLogoutRequest(string Server = "");
