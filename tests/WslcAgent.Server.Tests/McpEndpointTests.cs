using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;

namespace WslcAgent.Server.Tests;

/// <summary>
/// The MCP endpoint and the gate in front of its destructive tools. A client
/// only ever sees what JSON-RPC answers, so that is what these tests read.
/// </summary>
public sealed class McpEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>The tools are discovered from the assembly, so a whole area can go missing without a compile error.</summary>
    [Fact]
    public async Task The_endpoint_lists_every_tool_of_every_area()
    {
        var tools = await ToolsAsync(LocalClient(destructive: true));

        var names = tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).ToList();
        Assert.Contains("health", names);
        Assert.Contains("list_containers", names);
        Assert.Contains("list_images", names);
        Assert.Contains("list_volumes", names);
        Assert.Contains("list_networks", names);
        Assert.Contains("list_sessions", names);
        Assert.Contains("system_info", names);
        Assert.Contains("home_overview", names);
        Assert.Contains("remove_container", names);
        Assert.Contains("parse_compose", names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    /// <summary>A client gates the tools by their annotations: a read tool must not look destructive.</summary>
    [Fact]
    public async Task Read_tools_are_annotated_read_only_and_destructive_ones_are_not()
    {
        var tools = await ToolsAsync(LocalClient(destructive: true));

        Assert.True(Hint(tools, "list_containers", "readOnlyHint"));
        Assert.True(Hint(tools, "container_logs", "readOnlyHint"));
        Assert.True(Hint(tools, "remove_container", "destructiveHint"));
        Assert.True(Hint(tools, "exec_in_container", "destructiveHint"));
        Assert.False(Hint(tools, "remove_container", "readOnlyHint"));
    }

    [Fact]
    public async Task A_tool_answers_over_json_rpc()
    {
        var answer = await CallAsync(LocalClient(), "health", new { });

        var body = JsonDocument.Parse(Text(answer)).RootElement;
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    /// <summary>
    /// Switched off, the destructive tools are not there to be offered: a model
    /// cannot propose removing what this agent will not remove. The listing is
    /// filtered per request, so the operator's change needs no restart.
    /// </summary>
    [Fact]
    public async Task The_destructive_tools_are_not_listed_when_the_operator_turned_them_off()
    {
        var names = (await ToolsAsync(LocalClient()))
            .EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).ToList();

        Assert.DoesNotContain("remove_container", names);
        Assert.DoesNotContain("kill_container", names);
        Assert.DoesNotContain("exec_in_container", names);
        Assert.DoesNotContain("stop_session", names);
        Assert.Contains("list_containers", names);
        Assert.Contains("start_container", names);
    }

    /// <summary>And a client that kept the old list is refused all the same, with the reason.</summary>
    [Fact]
    public async Task A_destructive_tool_called_anyway_answers_that_it_is_disabled()
    {
        var answer = await CallAsync(LocalClient(), "remove_container", new { container = "web" });

        var body = JsonDocument.Parse(Text(answer)).RootElement;
        Assert.True(body.GetProperty("disabled").GetBoolean());
        Assert.Contains("switched off", body.GetProperty("message").GetString());
    }

    /// <summary>
    /// Settings → MCP server saves the switch and the endpoint obeys it on the
    /// next call, on the same running agent: that is the whole point of having
    /// it on a page instead of in a file the operator has to restart around.
    /// </summary>
    [Fact]
    public async Task Saving_the_switch_in_settings_changes_what_the_endpoint_offers()
    {
        var client = LocalClient();

        var before = await client.GetFromJsonAsync<McpStatus>("/api/v1/mcp/settings");
        Assert.NotNull(before);
        Assert.False(before.Settings.AllowDestructiveTools);
        Assert.EndsWith("/mcp", before.LocalUrl);
        Assert.True(before.DestructiveTools > 0);
        Assert.DoesNotContain("remove_container", await NamesAsync(client));

        var saved = await client.PutAsJsonAsync("/api/v1/mcp/settings", new McpSettings(Enabled: true, AllowDestructiveTools: true));
        var after = await saved.Content.ReadFromJsonAsync<McpStatus>();

        Assert.NotNull(after);
        Assert.True(after.Settings.AllowDestructiveTools);
        Assert.Equal(before.Tools + before.DestructiveTools, after.Tools);
        Assert.Contains("remove_container", await NamesAsync(client));
    }

    /// <summary>Switched off, there is nothing to list and nothing to call.</summary>
    [Fact]
    public async Task With_the_server_switched_off_no_tool_is_offered_or_runs()
    {
        var client = LocalClient();
        await client.PutAsJsonAsync("/api/v1/mcp/settings", new McpSettings(Enabled: false, AllowDestructiveTools: true));

        Assert.Empty(await NamesAsync(client));

        var answer = await CallAsync(client, "health", new { });
        Assert.True(answer.GetProperty("isError").GetBoolean());
        Assert.Contains("switched off", Text(answer));
    }

    /// <summary>
    /// One path, versioned with the API. The version is the tool surface's, not
    /// the protocol's — that one is negotiated in initialize — so the day a tool
    /// changes shape, /api/v1/mcp stays as it is and the new one is born beside
    /// it. Nothing answers anywhere else, so a client cannot be registered on a
    /// path that will not be versioned with it.
    /// </summary>
    [Fact]
    public async Task The_endpoint_answers_on_the_versioned_path_and_nowhere_else()
    {
        var client = LocalClient();

        Assert.NotEmpty(await NamesAsync(client, "/api/v1/mcp"));

        // Anything else is the UI's own page, not this server: a client pointed
        // there gets HTML, never a tool list.
        var elsewhere = await PostAsync(client, "tools/list", new { }, "/mcp");
        Assert.DoesNotContain("jsonrpc", await elsewhere.Content.ReadAsStringAsync());
    }

    /// <summary>The MCP path is API, not a page: a caller from another machine needs the token like any other.</summary>
    [Fact]
    public async Task A_remote_caller_without_a_token_is_refused()
    {
        var client = LocalClient();
        client.DefaultRequestHeaders.Add(TestHost.RemoteHeader, "1");

        var response = await PostAsync(client, "tools/list", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A caller at the agent's own machine, as every other endpoint test has
    /// one. The destructive tools ship off, so a test that wants to see them
    /// turns the operator's switch on for its own host.
    /// </summary>
    private HttpClient LocalClient(bool destructive = false) =>
        factory.WithWebHostBuilder(builder => builder.UseSetting("Mcp:AllowDestructiveTools", destructive ? "true" : "false"))
            .ClientWith(new FakeWslcRunner());

    /// <summary>The names a client would see right now.</summary>
    private static async Task<List<string?>> NamesAsync(HttpClient client, string path = McpPath) =>
        [.. (await ToolsAsync(client, path)).EnumerateArray().Select(tool => tool.GetProperty("name").GetString())];

    private static bool Hint(JsonElement tools, string name, string hint) =>
        tools.EnumerateArray().First(tool => tool.GetProperty("name").GetString() == name)
            .TryGetProperty("annotations", out var annotations)
            && annotations.TryGetProperty(hint, out var value) && value.GetBoolean();

    private static string Text(JsonElement answer) =>
        answer.GetProperty("content")[0].GetProperty("text").GetString() ?? "";

    /// <summary>The path a client registers, which is the versioned one.</summary>
    private const string McpPath = "/api/v1/mcp";

    private static async Task<JsonElement> ToolsAsync(HttpClient client, string path = McpPath) =>
        (await ResultAsync(client, "tools/list", new { }, path)).GetProperty("tools");

    private static async Task<JsonElement> CallAsync(HttpClient client, string name, object arguments) =>
        await ResultAsync(client, "tools/call", new { name, arguments });

    private static async Task<JsonElement> ResultAsync(HttpClient client, string method, object parameters, string path = McpPath)
    {
        var response = await PostAsync(client, method, parameters, path, await SessionAsync(client, path));
        response.EnsureSuccessStatusCode();

        // Streamable HTTP answers an event stream even for one call: "data: {…}".
        var payload = await response.Content.ReadAsStringAsync();
        var line = payload.Split('\n').FirstOrDefault(part => part.StartsWith("data: ", StringComparison.Ordinal));
        var json = JsonDocument.Parse(line is null ? payload : line["data: ".Length..]).RootElement;
        return json.GetProperty("result");
    }

    /// <summary>
    /// The handshake every real client does: the transport is stateful, because
    /// a destructive tool has to be able to ask the user through the client's own
    /// prompt, and a server with no session cannot ask anything. A call without a
    /// session is a 400, which is what a client that skips initialize deserves.
    /// </summary>
    private static async Task<string?> SessionAsync(HttpClient client, string path)
    {
        var initialize = await PostAsync(client, "initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "tests", version = "1" },
        }, path);
        initialize.EnsureSuccessStatusCode();
        var session = initialize.Headers.TryGetValues("Mcp-Session-Id", out var values) ? values.FirstOrDefault() : null;
        await PostAsync(client, "notifications/initialized", new { }, path, session);
        return session;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string method, object parameters, string path = McpPath, string? session = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = parameters }),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (session is not null)
        {
            request.Headers.Add("Mcp-Session-Id", session);
        }

        return client.SendAsync(request);
    }
}

/// <summary>
/// The two-call approval in front of the destructive tools, for a client that
/// cannot ask its own user. One that can is asked through its own prompt, which
/// is the other half of the gate and belongs to a client, not to a test.
/// </summary>
public sealed class ApprovalGateTests
{
    private static readonly Dictionary<string, string> Web = new() { ["container"] = "web" };
    private static readonly Dictionary<string, string> Db = new() { ["container"] = "db" };

    private static ApprovalGate Gate(bool on = true) => new(() => on);

    private static ValueTask<object?> CheckAsync(ApprovalGate gate, string target, string? confirm = null) =>
        gate.CheckAsync(null, "remove_container", $"remove container {target}", target,
            target == "web" ? Web : Db, confirm);

    [Fact]
    public async Task The_first_call_asks_and_does_not_act()
    {
        var answer = Assert.IsType<ApprovalRequired>(await CheckAsync(Gate(), "web"));

        Assert.Equal("remove container web", answer.Action);
        Assert.True(answer.ConfirmationRequired);
        Assert.Equal("web", answer.Target["container"]);
        Assert.False(string.IsNullOrWhiteSpace(answer.ConfirmToken));
        Assert.Contains(answer.ConfirmToken, answer.Instructions);
    }

    /// <summary>A token that comes back too soon is nobody's answer, and saying so must not spend it.</summary>
    [Fact]
    public async Task A_token_returned_before_anyone_could_answer_is_refused_and_survives()
    {
        var gate = Gate();
        var token = Assert.IsType<ApprovalRequired>(await CheckAsync(gate, "web")).ConfirmToken;

        var tooSoon = Assert.IsType<ApprovalRefused>(await CheckAsync(gate, "web", token));

        Assert.Contains("seconds after it was issued", tooSoon.Message);
        // Still the same token: only the pause is missing, not the approval.
        Assert.IsType<ApprovalRefused>(await CheckAsync(gate, "web", token));
    }

    /// <summary>A yes to one target is not a yes to another.</summary>
    [Fact]
    public async Task A_token_is_bound_to_its_target()
    {
        var gate = Gate();
        var token = Assert.IsType<ApprovalRequired>(await CheckAsync(gate, "web")).ConfirmToken;

        var other = Assert.IsType<ApprovalRefused>(await CheckAsync(gate, "db", token));

        Assert.Contains("different action or target", other.Message);
    }

    /// <summary>The user's first order, repeated by the model, is not the confirmation.</summary>
    [Theory]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("ok")]
    public async Task A_word_instead_of_a_token_is_refused(string confirm)
    {
        var refused = Assert.IsType<ApprovalRefused>(await CheckAsync(Gate(), "web", confirm));

        Assert.Contains("must be the confirmToken", refused.Message);
    }

    [Fact]
    public async Task An_unknown_token_is_refused()
    {
        var refused = Assert.IsType<ApprovalRefused>(await CheckAsync(Gate(), "web", "deadbeefdeadbeef"));

        Assert.Contains("Unknown or expired", refused.Message);
    }

    /// <summary>Switched off, a tool refuses in a sentence the model can pass on, not as a failure.</summary>
    [Fact]
    public async Task Disabled_refuses_with_a_reason()
    {
        var answer = Assert.IsType<ApprovalDisabled>(await CheckAsync(Gate(on: false), "web"));

        Assert.True(answer.Disabled);
        Assert.Contains("switched off", answer.Message);
    }

    /// <summary>The switch is read on every call: turning it off is in force at once.</summary>
    [Fact]
    public async Task Turning_the_switch_off_takes_effect_on_the_next_call()
    {
        var on = true;
        var gate = new ApprovalGate(() => on);
        Assert.IsType<ApprovalRequired>(await CheckAsync(gate, "web"));

        on = false;
        Assert.IsType<ApprovalDisabled>(await CheckAsync(gate, "web"));
    }
}
