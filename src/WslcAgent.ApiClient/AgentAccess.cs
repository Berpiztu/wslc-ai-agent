using System.Net;
using System.Net.Http.Headers;

namespace WslcAgent.ApiClient;

/// <summary>
/// The session a native client signed in with. A browser needs none: the agent's
/// session cookie travels on its own. A native client talks to the agent from
/// another origin, so it keeps the value and sends it as a bearer token (and on
/// WebSocket addresses, which carry no headers).
/// </summary>
public sealed class AgentAccessToken
{
    /// <summary>The session value, or null when this client has not signed in.</summary>
    public string? Value { get; set; }

    /// <summary>Raised when the agent answered that this client must sign in; the UI shows the sign-in screen.</summary>
    public event Action? SignInRequired;

    internal void RaiseSignInRequired() => SignInRequired?.Invoke();
}

/// <summary>Adds the bearer token to every call and reports a refused one to <see cref="AgentAccessToken.SignInRequired"/>.</summary>
public sealed class AgentAccessHandler(AgentAccessToken token) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (token.Value is { Length: > 0 } value)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value);
        }

        var response = await base.SendAsync(request, cancellationToken);
        // A wrong password on the sign-in call itself is that screen's own answer.
        if (response.StatusCode == HttpStatusCode.Unauthorized && request.RequestUri?.AbsolutePath.EndsWith("/api/v1/login", StringComparison.Ordinal) != true)
        {
            token.RaiseSignInRequired();
        }

        return response;
    }
}

/// <summary>Telling a refusal that sends the UI to sign in from a real failure.</summary>
public static class SignInErrors
{
    /// <summary>
    /// The agent answered 401: the sign-in screen is already on its way
    /// (<see cref="AgentAccessToken.SignInRequired"/>), so a page says nothing of its own.
    /// </summary>
    public static bool IsSignInRequired(this Exception exception) => exception switch
    {
        AgentApiException api => api.StatusCode == (int)HttpStatusCode.Unauthorized,
        HttpRequestException http => http.StatusCode == HttpStatusCode.Unauthorized,
        _ => false,
    };
}
