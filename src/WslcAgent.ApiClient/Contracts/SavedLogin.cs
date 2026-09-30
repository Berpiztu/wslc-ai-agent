namespace WslcAgent.ApiClient.Contracts;

/// <summary>
/// A login kept on the agent for the pages opened in the host browser: a title
/// to pick it by, and the email, the user and the password the host browser
/// pane shows to copy or to type into the page — a sign-in page asks for one or
/// the other, and a login that only had a user could not answer both. Body of
/// <c>GET /api/v1/saved-logins</c> (a list) and the answer of a create or an update.
/// </summary>
public sealed record SavedLogin(string Id, string Title, string Email, string User, string Password);

/// <summary>Body of <c>POST /api/v1/saved-logins</c> and <c>PUT /api/v1/saved-logins/{id}</c>.</summary>
public sealed record SavedLoginInput(string Title, string Email, string User, string Password);
