using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Tests;

/// <summary>The agent's own login: local callers pass, everyone else signs in.</summary>
public sealed class AgentAuthTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Sessions = "/api/v1/sessions";

    [Fact]
    public async Task A_remote_caller_is_refused_a_page_goes_to_sign_in_and_health_stays_open()
    {
        var client = Remote(factory.Agent(new FakeWslcRunner()).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));

        var api = await client.GetAsync(Sessions);
        var health = await client.GetAsync("/api/v1/health");
        using var page = new HttpRequestMessage(HttpMethod.Get, "/containers?all=1");
        page.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        var redirect = await client.SendAsync(page);

        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("/login?next=%2Fcontainers%3Fall%3D1", redirect.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Through_a_proxy_with_no_password_the_answer_says_how_to_set_one()
    {
        var client = factory.Agent(new FakeWslcRunner()).CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.9");

        var response = await client.GetAsync(Sessions);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("/settings", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_remote_client_signs_in_and_uses_the_session_as_a_bearer_token()
    {
        var agent = factory.Agent(new FakeWslcRunner().Answer("session list", "ID  CREATOR PID  NAME\n"));
        var local = agent.CreateClient();
        var remote = Remote(agent.CreateClient());

        Assert.Equal(HttpStatusCode.NoContent, (await local.PutAsJsonAsync("/api/v1/login/credentials", new SetCredentialsRequest("admin", "s3cret"))).StatusCode);
        var wrong = await remote.PostAsJsonAsync("/api/v1/login", new LoginRequest("admin", "nope"));
        var signedIn = await (await remote.PostAsJsonAsync("/api/v1/login", new LoginRequest("admin", "s3cret"))).Content.ReadFromJsonAsync<LoginResult>();
        remote.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn!.Token);
        var status = await remote.GetFromJsonAsync<LoginStatus>("/api/v1/login");
        var allowed = await remote.GetAsync(Sessions);

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.True(status!.SignedIn);
        Assert.NotEqual(HttpStatusCode.Unauthorized, allowed.StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_ends_the_sessions_issued_before()
    {
        var agent = factory.Agent(new FakeWslcRunner());
        var local = agent.CreateClient();
        var remote = Remote(agent.CreateClient());
        await local.PutAsJsonAsync("/api/v1/login/credentials", new SetCredentialsRequest("admin", "first"));
        var token = (await (await remote.PostAsJsonAsync("/api/v1/login", new LoginRequest("admin", "first"))).Content.ReadFromJsonAsync<LoginResult>())!.Token;

        await local.PutAsJsonAsync("/api/v1/login/credentials", new SetCredentialsRequest("admin", "second"));
        remote.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.Unauthorized, (await remote.GetAsync(Sessions)).StatusCode);
    }

    private static HttpClient Remote(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(TestHost.RemoteHeader, "1");
        return client;
    }
}
