using System.Net;
using WslcAgent.ApiClient;

namespace WslcAgent.Server.Tests;

/// <summary>The link to the agent, read from every call the client makes.</summary>
public sealed class AgentLinkTests
{
    [Fact]
    public async Task A_call_that_cannot_be_sent_takes_the_link_down_and_any_answer_brings_it_back()
    {
        var link = new AgentLink();
        var agent = new ScriptedAgent();
        using var client = new HttpClient(new AgentLinkHandler(link) { InnerHandler = agent }) { BaseAddress = new Uri("http://agent/") };
        var changes = 0;
        link.Changed += () => changes++;

        agent.Answer = null;
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("api/v1/health"));
        Assert.False(link.Online);

        // A refusal is the agent talking: the link is up, whatever it said.
        agent.Answer = HttpStatusCode.ServiceUnavailable;
        await client.GetAsync("api/v1/containers");
        Assert.True(link.Online);
        Assert.Equal(2, changes);
    }

    /// <summary>Android's handler throws a cut connection as a WebException; every screen catches HttpRequestException, so that is what it becomes.</summary>
    [Fact]
    public async Task A_connection_cut_under_a_request_on_Android_is_nothing_having_answered()
    {
        var link = new AgentLink();
        using var client = new HttpClient(new AgentLinkHandler(link) { InnerHandler = new CutConnection() }) { BaseAddress = new Uri("http://agent/") };

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("api/v1/images"));

        Assert.IsType<WebException>(failure.InnerException);
        Assert.False(link.Online);
    }

    /// <summary>What Android's own handler throws when a connection is cut under a request.</summary>
    private sealed class CutConnection : HttpMessageHandler
    {
#pragma warning disable SYSLIB0014 // WebException is what the platform throws, not something this code creates by choice.
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new WebException("Canceled");
#pragma warning restore SYSLIB0014
    }

    /// <summary>Answers with the status it is given, or fails to be reached at all.</summary>
    private sealed class ScriptedAgent : HttpMessageHandler
    {
        public HttpStatusCode? Answer { get; set; } = HttpStatusCode.OK;

        // The agent's own refusals are problem details: a bare 502, 503 or 504
        // is a gateway answering for an agent that is not there.
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Answer is { } status
                ? Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/problem+json") })
                : throw new HttpRequestException("TypeError: Failed to fetch");
    }
}
