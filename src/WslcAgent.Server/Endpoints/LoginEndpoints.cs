using Microsoft.AspNetCore.Http.HttpResults;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Auth;
using WslcAgent.Server.Host;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/login</c> and <c>/api/v1/logout</c>: sign in, sessions for native clients, the Internet login and the API token. See docs/api-v1.md.</summary>
public static class LoginEndpoints
{
    public static RouteGroupBuilder MapLoginEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/login", (HttpContext http, AgentLogin login) =>
                new LoginStatus(login.IsConfigured, login.Username, login.SessionUser(AgentAuth.SessionValue(http)) is not null, LocalClient.IsLocal(http), login.HasApiToken))
            .WithName("LoginStatus");

        api.MapPost("/login", Results<Ok<LoginResult>, ProblemHttpResult> (HttpContext http, LoginRequest request, AgentLogin login) =>
            {
                if (!login.IsConfigured)
                {
                    return TypedResults.Problem(AgentAuth.SetupHint(http), statusCode: StatusCodes.Status503ServiceUnavailable, title: "Internet login not set");
                }

                if (!login.CredentialsMatch(request.Username, request.Password))
                {
                    return TypedResults.Problem("Invalid username or password.", statusCode: StatusCodes.Status401Unauthorized, title: "Sign in failed");
                }

                return TypedResults.Ok(Issue(http, login));
            })
            .WithName("Login");

        // A caller already let in (a native client's token, or the agent's own machine) gets a
        // session of its own: what an embedded page needs, since a WebSocket carries no headers.
        api.MapPost("/login/session", (HttpContext http, AgentLogin login) => Issue(http, login))
            .WithName("LoginSession");

        api.MapPost("/logout", NoContent (HttpContext http) =>
            {
                AgentAuth.ClearSessionCookie(http);
                return TypedResults.NoContent();
            })
            .WithName("Logout");

        api.MapPut("/login/credentials", NoContent (SetCredentialsRequest request, AgentLogin login) =>
            {
                login.SetCredentials(request.Username, request.Password);
                return TypedResults.NoContent();
            })
            .WithName("SetLoginCredentials");

        api.MapPost("/login/api-token", (AgentLogin login) => new ApiTokenResult(login.RenewApiToken()))
            .WithName("RenewApiToken");

        return api;
    }

    private static LoginResult Issue(HttpContext http, AgentLogin login)
    {
        var token = login.IssueSession();
        AgentAuth.SetSessionCookie(http, token);
        return new LoginResult(token, login.Username);
    }
}
