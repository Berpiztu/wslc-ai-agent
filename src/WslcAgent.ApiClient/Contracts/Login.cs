namespace WslcAgent.ApiClient.Contracts;

/// <summary>Body of <c>GET /api/v1/login</c>: what the sign-in screen and the Settings card show.</summary>
/// <param name="Configured">A password is set, so signing in from outside the agent's machine is possible.</param>
/// <param name="Username">The username to sign in with.</param>
/// <param name="SignedIn">The caller holds a valid session (cookie or bearer), so Log out means something.</param>
/// <param name="Local">The caller sits at the agent's machine and needs no login.</param>
/// <param name="ApiToken">An API token is set for scripts and AI agents.</param>
public sealed record LoginStatus(bool Configured, string Username, bool SignedIn, bool Local, bool ApiToken);

/// <summary>Body of <c>POST /api/v1/login</c>.</summary>
public sealed record LoginRequest(string Username, string Password);

/// <summary>
/// Answer of <c>POST /api/v1/login</c> and <c>POST /api/v1/login/session</c>: the session
/// value, also set as the agent's session cookie. A native client sends it as
/// <c>Authorization: Bearer</c>, and as <c>access_token</c> on a WebSocket address.
/// </summary>
public sealed record LoginResult(string Token, string Username);

/// <summary>Body of <c>PUT /api/v1/login/credentials</c>: the Internet login's username and password.</summary>
public sealed record SetCredentialsRequest(string Username, string Password);

/// <summary>Answer of <c>POST /api/v1/login/api-token</c>: a new token, shown once.</summary>
public sealed record ApiTokenResult(string Token);
