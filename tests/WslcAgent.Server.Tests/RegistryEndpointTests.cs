using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

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
    public async Task A_login_is_listed_without_its_password_until_its_logout()
    {
        var runner = new FakeWslcRunner().Answer("login --username me --password-stdin GHCR.io", "Login Succeeded").Answer("logout ghcr.io", "");
        var agent = factory.Agent(runner);
        var client = agent.CreateClient();

        await client.PostAsJsonAsync("/api/v1/registry/login", new RegistryLoginRequest("GHCR.io", "me", "s3cret"));
        var listed = await client.GetFromJsonAsync<List<RegistryLogin>>("/api/v1/registry/logins");
        var dataDirectory = agent.Services.GetRequiredService<IOptions<WslcOptions>>().Value.DataDirectory;
        var file = await File.ReadAllTextAsync(Path.Combine(dataDirectory, "registry-logins.json"));
        await client.PostAsJsonAsync("/api/v1/registry/logout", new RegistryLogoutRequest("ghcr.io"));
        var afterLogout = await client.GetFromJsonAsync<List<RegistryLogin>>("/api/v1/registry/logins");

        var only = Assert.Single(listed!);
        Assert.Equal(("ghcr.io", "me"), (only.Server, only.Username));
        Assert.DoesNotContain("s3cret", file);
        Assert.Empty(afterLogout!);
    }

    [Fact]
    public async Task A_login_that_fails_is_not_listed()
    {
        var client = factory.ClientWith(new FakeWslcRunner().Fail("login --username me --password-stdin ghcr.io", "unauthorized"));

        await client.PostAsJsonAsync("/api/v1/registry/login", new RegistryLoginRequest("ghcr.io", "me", "wrong"));
        var listed = await client.GetFromJsonAsync<List<RegistryLogin>>("/api/v1/registry/logins");

        Assert.Empty(listed!);
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
