using System.Net;

namespace WslcAgent.ApiClient;

/// <summary>
/// Whether the agent answers. The application is not made to work without it:
/// when a call cannot be sent at all — the agent stopped, the network went — the
/// screen waits for it instead of every page and every poll repeating "Failed
/// to fetch" on its own. <see cref="AgentLinkHandler"/> keeps this up to date
/// from every call; the layout shows the wait and asks for the agent's health
/// until it answers, and every screen reads again when it does.
/// </summary>
public sealed class AgentLink
{
    /// <summary>True until a call could not be sent; true again the moment any call is answered.</summary>
    public bool Online { get; private set; } = true;

    /// <summary>Raised when the link went down or came back.</summary>
    public event Action? Changed;

    internal void Set(bool online)
    {
        if (online == Online)
        {
            return;
        }

        Online = online;
        Changed?.Invoke();
    }
}

/// <summary>Reads the link from every call: a call that could not be sent takes it down, any answer brings it back.</summary>
public sealed class AgentLinkHandler(AgentLink link) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            // No status: nothing answered (the browser's "Failed to fetch"). A
            // refusal with a status is the agent talking, which is not this.
            link.Set(false);
            throw;
        }
        catch (Exception ex) when (ex is WebException or IOException)
        {
            // The same failure as Android's own handler throws it: a connection
            // cut under a request (the app relaunched, the tunnel reopened) is a
            // WebException "Canceled", not the HttpRequestException every screen
            // catches, and it brought the Images page down to the error panel.
            // Our own
            // cancellation stays one; anything else is nothing having answered.
            cancellationToken.ThrowIfCancellationRequested();
            link.Set(false);
            throw new HttpRequestException(ex.Message, ex);
        }

        link.Set(!SpokenFor(response));
        return response;
    }

    /// <summary>
    /// A gateway answered for an agent that is not there (an address whose agent had not been started answered
    /// 502 to every call, and the application drew itself around the error).
    /// The status alone does not say it — the agent answers 502 itself when a
    /// <c>wslc</c> command fails, 503 when <c>wslc</c> is missing and 504 when
    /// it times out — but the body does: the agent's own refusals are always
    /// problem details, and a proxy's are its own page.
    /// </summary>
    private static bool SpokenFor(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
        && response.Content.Headers.ContentType?.MediaType != "application/problem+json";
}
