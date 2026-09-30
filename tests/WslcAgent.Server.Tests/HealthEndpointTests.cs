using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
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
}
