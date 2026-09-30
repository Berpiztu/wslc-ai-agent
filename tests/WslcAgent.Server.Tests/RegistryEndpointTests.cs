using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Tests;

public sealed class RegistryEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Login_sends_the_password_on_stdin_never_in_the_arguments()
    {
        var runner = new FakeWslcRunner().Answer("login --username me --password-stdin ghcr.io", "Login Succeeded");

        var response = await factory.ClientWith(runner).PostAsJsonAsync("/api/v1/registry/login", new RegistryLoginRequest("ghcr.io", "me", "s3cret"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain("s3cret", string.Join(' ', Assert.Single(runner.Calls)));
        Assert.Equal("s3cret", Assert.Single(runner.Inputs));
    }

    [Fact]
    public async Task Login_and_logout_leave_out_what_was_not_given()
    {
        var runner = new FakeWslcRunner().Answer("login", "").Answer("logout", "").Answer("logout ghcr.io", "");
        var client = factory.ClientWith(runner);

        await client.PostAsJsonAsync("/api/v1/registry/login", new RegistryLoginRequest());
        await client.PostAsJsonAsync("/api/v1/registry/logout", new RegistryLogoutRequest());
        var rejected = await client.PostAsJsonAsync("/api/v1/registry/logout", new RegistryLogoutRequest("--all"));
        await client.PostAsJsonAsync("/api/v1/registry/logout", new RegistryLogoutRequest("ghcr.io"));

        Assert.Equal(["login", "logout", "logout ghcr.io"], runner.Calls.Select(c => string.Join(' ', c)));
        Assert.Null(runner.Inputs[0]);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }
}
