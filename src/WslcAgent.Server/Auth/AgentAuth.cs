using Microsoft.AspNetCore.Mvc;
using WslcAgent.Server.Endpoints;
using WslcAgent.Server.Host;

namespace WslcAgent.Server.Auth;

/// <summary>
/// Who may use the agent: a signed-in session (the
/// cookie a browser keeps, or the same value as a bearer token from a native
/// client), the API token, or someone sitting at the agent's own machine and
/// talking to it directly. A request that came through a proxy is never local,
/// whatever address it arrives from.
/// </summary>
public static class AgentAuth
{
    public const string SessionCookie = "wslc_agent_session";

    /// <summary>A WebSocket from a browser cannot carry headers: a native client puts its token here instead.</summary>
    private const string AccessTokenQuery = "access_token";

    /// <summary>What anyone may reach: the sign-in screen and its calls, the health check, and the UI's static files.</summary>
    public static bool IsPublic(HttpRequest request)
    {
        var path = request.Path.Value ?? "/";
        if (path is "/login" or "/api/v1/health" or "/api/v1/logout" || (path == "/api/v1/login" && (HttpMethods.IsGet(request.Method) || HttpMethods.IsPost(request.Method))))
        {
            return true;
        }

        return !IsApi(path) && System.IO.Path.HasExtension(path) && !path.EndsWith(".html", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><c>user:NAME</c>, <c>token</c> or <c>local</c> for an allowed caller; null otherwise.</summary>
    public static string? Identify(HttpContext http, AgentLogin login)
    {
        if (login.SessionUser(SessionValue(http)) is { } user)
        {
            return $"user:{user}";
        }

        if (BearerOrHeader(http) is { Length: > 0 } token && login.ApiTokenMatches(token))
        {
            return "token";
        }

        return LocalClient.IsLocal(http) ? "local" : null;
    }

    /// <summary>The session value the caller presents: the cookie, a bearer token, or a WebSocket's <c>access_token</c>.</summary>
    public static string? SessionValue(HttpContext http) =>
        http.Request.Cookies.TryGetValue(SessionCookie, out var cookie) && cookie.Length > 0 ? cookie : BearerOrHeader(http);

    /// <summary>The session cookie, lasting as long as the session and marked secure behind HTTPS.</summary>
    public static void SetSessionCookie(HttpContext http, string value) =>
        http.Response.Cookies.Append(SessionCookie, value, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = IsHttps(http),
            MaxAge = AgentLogin.SessionLifetime,
            Path = "/",
        });

    public static void ClearSessionCookie(HttpContext http) => http.Response.Cookies.Delete(SessionCookie, new CookieOptions { Path = "/" });

    /// <summary>
    /// Pages go to the sign-in screen and come back afterwards; calls get a 401. Through
    /// a proxy with no password set nobody could ever sign in, so the answer says how
    /// to set one instead.
    /// </summary>
    public static Task DenyAsync(HttpContext http, AgentLogin login)
    {
        var path = http.Request.Path.Value ?? "/";
        if (!IsApi(path) && http.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            http.Response.Redirect($"/login?next={Uri.EscapeDataString(path + http.Request.QueryString)}");
            return Task.CompletedTask;
        }

        var (status, title, detail) = Proxied(http) && !login.IsConfigured && !login.HasApiToken
            ? (StatusCodes.Status503ServiceUnavailable, "Internet login not set", SetupHint(http))
            : (StatusCodes.Status401Unauthorized, "Sign in required", "Sign in at /login, or send Authorization: Bearer <session or API token>.");
        http.Response.StatusCode = status;
        return ProblemMapping.WriteAsync(http.Response, new ProblemDetails { Status = status, Title = title, Detail = detail });
    }

    /// <summary>How to set the Internet login when it is missing: on the agent's machine, where no login is asked.</summary>
    public static string SetupHint(HttpContext http) =>
        $"This agent has no Internet login yet. On the agent's machine, open http://127.0.0.1:{http.Connection.LocalPort}/settings (no login is asked there), set the Internet username and password, then come back to this address.";

    /// <summary>The <c>next</c> rule: only a path of this agent, never another site.</summary>
    public static string SafeNext(string? next) =>
        next is { Length: > 0 } value && value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal) && !value.StartsWith("/\\", StringComparison.Ordinal) ? value : "/";

    private static string? BearerOrHeader(HttpContext http)
    {
        var authorization = http.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authorization[7..].Trim();
        }

        if (http.Request.Headers["X-WSLC-Token"].ToString() is { Length: > 0 } header)
        {
            return header.Trim();
        }

        return http.WebSockets.IsWebSocketRequest && http.Request.Query[AccessTokenQuery].ToString() is { Length: > 0 } query ? query : null;
    }

    private static bool IsApi(string path) =>
        path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase);

    private static bool Proxied(HttpContext http) =>
        http.Request.Headers.ContainsKey("X-Forwarded-For") || http.Request.Headers.ContainsKey("X-Forwarded-Proto") || http.Request.Headers.ContainsKey("Forwarded");

    private static bool IsHttps(HttpContext http) =>
        http.Request.Headers["X-Forwarded-Proto"].ToString() is { Length: > 0 } proto
            ? proto.Split(',')[0].Trim().Equals("https", StringComparison.OrdinalIgnoreCase)
            : http.Request.IsHttps;
}

/// <summary>Lets through the public paths and allowed callers; everything else is denied as <see cref="AgentAuth.DenyAsync"/> says.</summary>
public sealed class AgentAuthMiddleware(RequestDelegate next, AgentLogin login)
{
    public Task InvokeAsync(HttpContext http) =>
        AgentAuth.IsPublic(http.Request) || AgentAuth.Identify(http, login) is not null
            ? next(http)
            : AgentAuth.DenyAsync(http, login);
}
