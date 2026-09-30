using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Tests;

/// <summary>Settings → Testing: the simulate-remote switch stays as the user left it.</summary>
public sealed class TestingEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Simulate_remote_is_off_until_switched_on_and_then_stays_on()
    {
        var client = factory.ClientWith(new FakeWslcRunner());

        var initial = await client.GetFromJsonAsync<TestingSettings>("/api/v1/testing");
        var saved = await (await client.PutAsJsonAsync("/api/v1/testing", new TestingSettings(true))).Content.ReadFromJsonAsync<TestingSettings>();
        var reread = await client.GetFromJsonAsync<TestingSettings>("/api/v1/testing");

        Assert.False(initial!.SimulateRemote);
        Assert.True(saved!.SimulateRemote);
        Assert.True(reread!.SimulateRemote);
    }
}
