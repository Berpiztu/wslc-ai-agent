using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Tests;

public sealed class HealthEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_reports_ok_and_a_version()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);
        Assert.False(string.IsNullOrWhiteSpace(body.Version));
    }

    /// <summary>
    /// The build is what a browser watches to know it is no longer running the
    /// code the agent serves; the version cannot do it, since it is written by
    /// hand and stays the same through a morning of rebuilds.
    /// </summary>
    [Fact]
    public async Task Health_names_the_build_it_is_answering_from()
    {
        var client = factory.CreateClient();

        var body = await client.GetFromJsonAsync<HealthResponse>("/api/v1/health");

        Assert.NotNull(body);
        Assert.Equal(32, body.Build.Length);
        Assert.NotEqual(body.Version, body.Build);
    }

    /// <summary>
    /// A native client checks itself against these: an agent that stopped
    /// saying them would let an incompatible client run without a word.
    /// </summary>
    [Fact]
    public async Task Health_says_its_api_level_and_the_oldest_client_it_serves()
    {
        var body = await factory.CreateClient().GetFromJsonAsync<HealthResponse>("/api/v1/health");

        Assert.NotNull(body);
        Assert.Equal(ApiCompatibility.Level, body.ApiLevel);
        Assert.Equal(ApiCompatibility.MinimumClientLevel, body.MinimumClientApiLevel);
        Assert.Equal(ApiMatch.Same, ApiCompatibility.Compare(body));
    }

    /// <summary>Either side may be the old one; an agent too old to say its level is never judged.</summary>
    [Theory]
    [InlineData(0, 0, ApiMatch.Unknown)]
    [InlineData(ApiCompatibility.Level + 1, ApiCompatibility.Level + 1, ApiMatch.ClientTooOld)]
    [InlineData(ApiCompatibility.Level + 1, ApiCompatibility.Level, ApiMatch.ClientBehind)]
    public void A_client_and_an_agent_are_matched_by_their_levels(int agentLevel, int minimumClient, ApiMatch expected)
    {
        var health = new HealthResponse("ok", "9.9.9", ApiLevel: agentLevel, MinimumClientApiLevel: minimumClient);

        Assert.Equal(expected, ApiCompatibility.Compare(health));
    }
}
