using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

public sealed class SessionsEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SystemInfo =
        """{"Client":{"Version":"2.9.11.0"},"Server":{"SessionManagerVersion":"2.9.11","Sessions":[{"CreatorPid":1,"ID":1,"Name":"wslc-cli-user"}]}}""";

    [Fact]
    public async Task Sessions_lists_the_running_sessions_and_the_selection_follows_select()
    {
        var runner = new FakeWslcRunner().Answer("system info --format json", SystemInfo);
        var client = factory.ClientWith(runner);

        var before = await client.GetFromJsonAsync<SessionsResponse>("/api/v1/sessions");
        var select = await client.PostAsJsonAsync("/api/v1/sessions/select", new SelectSessionRequest("wslc-cli-user"));
        var after = await client.GetFromJsonAsync<SessionsResponse>("/api/v1/sessions");

        Assert.NotNull(before);
        Assert.Contains(before.Sessions, s => s.Name == "wslc-cli-user" && s.Active && s.Reserved && s.CanStart);
        // Nothing chosen is not nothing targeted: the CLI's own store for this user.
        Assert.Equal(SessionStores.Default, before.Selected);
        Assert.Equal(HttpStatusCode.NoContent, select.StatusCode);
        Assert.Equal("wslc-cli-user", after!.Selected);
    }

    [Fact]
    public async Task The_elevated_store_is_never_offered_as_a_session()
    {
        // The agent does not run elevated and has no way to: a row for that
        // store could only be picked to be told no.
        const string withAdmin =
            """{"Server":{"Sessions":[{"ID":1,"Name":"wslc-cli-user"},{"ID":2,"Name":"wslc-cli-admin-root"}]}}""";
        var runner = new FakeWslcRunner().Answer("system info --format json", withAdmin);
        var client = factory.ClientWith(runner);

        var sessions = await client.GetFromJsonAsync<SessionsResponse>("/api/v1/sessions");

        Assert.NotNull(sessions);
        Assert.Contains(sessions.Sessions, s => s.Name == "wslc-cli-user");
        Assert.DoesNotContain(sessions.Sessions, s => SessionStores.IsAdmin(s.Name));
    }

    [Fact]
    public async Task Stopping_the_session_terminates_it_and_says_so()
    {
        var runner = new FakeWslcRunner().Answer("system info --format json", SystemInfo);
        // The stop is only a stop once wslc stops listing the session: the terminate takes it off the list.
        runner.AnswerWhen(
            args => string.Join(' ', args) == "system session terminate",
            _ => runner.Answer("system info --format json", """{"Server":{"Sessions":[]}}"""));
        var client = factory.ClientWith(runner);

        var response = await client.PostAsJsonAsync("/api/v1/sessions/stop", new SessionActionRequest("wslc-cli-user"));
        var result = await response.Content.ReadFromJsonAsync<SessionActionResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(result!.Changed);
        Assert.Equal("Stopped Session wslc-cli-user.", result.Message);
        Assert.Contains(runner.Calls, call => string.Join(' ', call) == "system session terminate");
        // The verb carries its own target instead of the agent's selection.
        Assert.Contains("wslc-cli-user", runner.Sessions);
    }

    [Fact]
    public async Task Stopping_a_session_that_is_not_running_changes_nothing()
    {
        var runner = new FakeWslcRunner().Answer("system info --format json", SystemInfo);
        var client = factory.ClientWith(runner);

        var response = await client.PostAsJsonAsync("/api/v1/sessions/stop", new SessionActionRequest("not-running"));
        var result = await response.Content.ReadFromJsonAsync<SessionActionResult>();

        Assert.False(result!.Changed);
        Assert.Equal("Session not-running is not running.", result.Message);
        Assert.DoesNotContain(runner.Calls, call => string.Join(' ', call) == "system session terminate");
    }

    [Fact]
    public async Task Starting_a_session_that_already_runs_leaves_it_alone()
    {
        var runner = new FakeWslcRunner().Answer("system info --format json", SystemInfo);
        var client = factory.ClientWith(runner);

        var response = await client.PostAsJsonAsync("/api/v1/sessions/start", new SessionActionRequest("wslc-cli-user"));
        var result = await response.Content.ReadFromJsonAsync<SessionActionResult>();

        Assert.False(result!.Changed);
        Assert.Equal("Session wslc-cli-user is already running.", result.Message);
        Assert.DoesNotContain(runner.Calls, call => string.Join(' ', call) == "images --format json");
    }

    [Fact]
    public async Task Starting_a_reserved_session_that_does_not_open_says_where_to_start_it()
    {
        var runner = new FakeWslcRunner()
            .Answer("system info --format json", SystemInfo)
            .Answer("images --format json", "[]");
        var client = factory.ClientWith(runner);

        var response = await client.PostAsJsonAsync("/api/v1/sessions/start", new SessionActionRequest("wslc-cli-admin-root"));
        var problem = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("elevated wslc process", problem);
        // The default store is opened first, with no session of its own.
        Assert.Contains(runner.Calls, call => string.Join(' ', call) == "images --format json");
    }
}
