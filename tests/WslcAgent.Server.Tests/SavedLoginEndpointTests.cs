using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

/// <summary>The saved logins the host browser pane picks from: kept encrypted on the agent, titles unique.</summary>
public sealed class SavedLoginEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/api/v1/saved-logins";

    [Fact]
    public async Task A_login_is_created_listed_changed_and_removed_and_the_file_does_not_hold_it_in_the_clear()
    {
        var agent = factory.Agent(new FakeWslcRunner());
        var client = agent.CreateClient();

        var create = await client.PostAsJsonAsync(Path, new SavedLoginInput("Open WebUI", "admin@example.com", "admin", "s3cret-Value"));
        var created = await create.Content.ReadFromJsonAsync<SavedLogin>();
        var update = await client.PutAsJsonAsync($"{Path}/{created!.Id}", new SavedLoginInput("Open WebUI", "admin@example.com", "admin", "changed-Value"));
        var listed = await client.GetFromJsonAsync<List<SavedLogin>>(Path);
        var dataDirectory = agent.Services.GetRequiredService<IOptions<WslcOptions>>().Value.DataDirectory;
        var file = await File.ReadAllTextAsync(System.IO.Path.Combine(dataDirectory, "saved-logins.json"));
        var delete = await client.DeleteAsync($"{Path}/{created.Id}");
        var afterDelete = await client.GetFromJsonAsync<List<SavedLogin>>(Path);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var only = Assert.Single(listed!);
        Assert.Equal(("Open WebUI", "admin@example.com", "admin", "changed-Value"), (only.Title, only.Email, only.User, only.Password));
        Assert.Contains("Open WebUI", file);
        Assert.DoesNotContain("changed-Value", file);
        Assert.DoesNotContain("admin@example.com", file);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(afterDelete!);
    }

    [Fact]
    public async Task A_title_already_used_is_a_conflict_and_a_missing_one_is_refused()
    {
        var client = factory.ClientWith(new FakeWslcRunner());

        await client.PostAsJsonAsync(Path, new SavedLoginInput("Grafana", "", "admin", "one"));
        var duplicate = await client.PostAsJsonAsync(Path, new SavedLoginInput("grafana", "", "other", "two"));
        var untitled = await client.PostAsJsonAsync(Path, new SavedLoginInput("  ", "user@example.com", "user", "pass"));
        var missing = await client.DeleteAsync($"{Path}/nope");

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, untitled.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task A_remote_caller_without_a_session_cannot_read_them()
    {
        var client = factory.ClientWith(new FakeWslcRunner());
        client.DefaultRequestHeaders.Add(TestHost.RemoteHeader, "1");

        var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
