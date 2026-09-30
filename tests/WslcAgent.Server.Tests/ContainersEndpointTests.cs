using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Containers;

namespace WslcAgent.Server.Tests;

public sealed class ContainersEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Inspect = "container inspect web --format json";

    [Fact]
    public async Task Containers_endpoint_returns_the_mapped_rows()
    {
        var runner = new FakeWslcRunner()
            .Answer("container list --all --format json", FakeWslcRunner.Fixture("container-list.ndjson"))
            .Answer("container stats --all --format json", FakeWslcRunner.Fixture("container-stats.ndjson"));
        var client = factory.ClientWith(runner);

        var response = await client.GetAsync("/api/v1/containers?all=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ContainerListResponse>();
        Assert.NotNull(body);
        Assert.Equal(["jade_wasatch", "agent-zero"], body.Containers.Select(r => r.Name));
        Assert.Equal(0.4, body.Aggregate.CpuUsedPercent);
        var agentZero = body.Containers.Single(r => r.Name == "agent-zero");
        Assert.Equal("1.2MB / 0B", agentZero.DiskIo);
        Assert.Equal("3.1kB / 2.2kB", agentZero.NetIo);
        Assert.Equal(1_200_000, agentZero.DiskIoBytes);
        Assert.Equal(5_300, agentZero.NetIoBytes);
        Assert.Equal((0L, 0L), (body.Containers.Single(r => r.Name == "jade_wasatch").DiskIoBytes, body.Containers.Single(r => r.Name == "jade_wasatch").NetIoBytes));
    }

    [Fact]
    public async Task A_failing_wslc_command_becomes_a_502_problem_with_its_message()
    {
        var runner = new FakeWslcRunner()
            .Fail("container list --all --format json", "Error: no active session\n")
            .Answer("container stats --all --format json", "");
        var client = factory.ClientWith(runner);

        var response = await client.GetAsync("/api/v1/containers");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("wslc command failed", problem.Title);
        Assert.Contains("no active session", problem.Detail);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("restart")]
    [InlineData("kill")]
    public async Task Lifecycle_endpoints_run_the_matching_wslc_command(string verb)
    {
        var runner = new FakeWslcRunner().Answer($"container {verb} agent-zero", "");
        var client = factory.ClientWith(runner);

        var response = await client.PostAsync($"/api/v1/containers/agent-zero/{verb}", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["container", verb, "agent-zero"], runner.Calls.Single());
    }

    [Fact]
    public async Task Delete_removes_the_container()
    {
        var runner = new FakeWslcRunner().Answer("container rm agent-zero", "");
        var client = factory.ClientWith(runner);

        var response = await client.DeleteAsync("/api/v1/containers/agent-zero");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["container", "rm", "agent-zero"], runner.Calls.Single());
    }

    [Fact]
    public async Task A_container_that_looks_like_an_option_is_rejected()
    {
        var client = factory.ClientWith(new FakeWslcRunner());

        var response = await client.PostAsync("/api/v1/containers/--rm/start", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Run_launches_detached_connects_extra_networks_and_enrols_the_policy()
    {
        var runner = new FakeWslcRunner()
            .Answer("container run --detach --name web --publish 85:80 nginx", "5b5598e8c4fd7a59ee04c1a96fec4a7c858668512c3a0605393b8110e24e0b1f\n")
            .Answer("network connect --ip 172.28.0.5 appnet web", "")
            .Answer(Inspect, FakeWslcRunner.Fixture("container-inspect.json"));
        var client = factory.ClientWith(runner);
        var request = new ContainerLaunchRequest { Image = "nginx", Name = "web", Publish = ["85:80"], ConnectNetworks = ["appnet 172.28.0.5"], RestartPolicy = "always" };

        var response = await client.PostAsJsonAsync("/api/v1/containers/run", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ContainerCreated>();
        Assert.Equal("5b5598e8c4fd", created!.Id);
        var policy = await client.GetFromJsonAsync<RestartPolicyInfo>("/api/v1/containers/web/restart-policy");
        Assert.Equal(new RestartPolicyInfo("always", "running", true), policy);
    }

    [Fact]
    public async Task Create_only_creates_and_a_missing_image_is_rejected()
    {
        var runner = new FakeWslcRunner().Answer("container create alpine sleep infinity", "abcdefabcdef\n");
        var client = factory.ClientWith(runner);

        var created = await client.PostAsJsonAsync("/api/v1/containers", new ContainerLaunchRequest { Image = "alpine", Command = "sleep infinity" });
        var rejected = await client.PostAsJsonAsync("/api/v1/containers", new ContainerLaunchRequest { Image = " " });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(["container", "create", "alpine", "sleep", "infinity"], runner.Calls.Single());
    }

    /// <summary>
    /// The container is removed only after its new settings have run under a
    /// throwaway name: the rehearsal is the whole launch, ports and volumes
    /// included, so what fails there fails before anything is destroyed.
    /// </summary>
    [Fact]
    public async Task Recreate_rehearses_before_it_removes_and_launches_again()
    {
        var rehearsal = ContainerService.RehearsalName("web");
        var runner = new FakeWslcRunner()
            .Answer(Inspect, FakeWslcRunner.Fixture("container-inspect.json"))
            .Answer("container stop web", "")
            .Answer($"container rm --force {rehearsal}", "")
            .Answer($"container run --detach --name {rehearsal} --env A=1 nginx", "beefbeefbeef\n")
            .Answer("container rm --force web", "")
            .Answer("container run --detach --name web --env A=1 nginx", "cafecafecafe\n");
        var client = factory.ClientWith(runner);

        var response = await client.PostAsJsonAsync("/api/v1/containers/web/recreate", new ContainerLaunchRequest { Image = "nginx", Name = "web", Env = ["A=1"] });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["container", "inspect", "web", "--format", "json"], runner.Calls[0]);
        Assert.Equal(["container", "stop", "web"], runner.Calls[1]);
        Assert.Equal(["container", "rm", "--force", rehearsal], runner.Calls[2]);
        Assert.Equal(["container", "run", "--detach", "--name", rehearsal, "--env", "A=1", "nginx"], runner.Calls[3]);
        Assert.Equal(["container", "rm", "--force", rehearsal], runner.Calls[4]);
        Assert.Equal(["container", "rm", "--force", "web"], runner.Calls[5]);
        Assert.Equal(["container", "run", "--detach", "--name", "web", "--env", "A=1", "nginx"], runner.Calls[6]);
        Assert.Equal("cafecafecafe", (await response.Content.ReadFromJsonAsync<ContainerCreated>())!.Id);
    }

    /// <summary>
    /// An entrypoint the image does not have once cost a container: the launch
    /// failed after the remove, and the restore, built the same way, failed too.
    /// Now the rehearsal fails instead, the container is started again, and the
    /// error says nothing changed.
    /// </summary>
    [Fact]
    public async Task A_recreate_whose_rehearsal_fails_leaves_the_container_as_it_was()
    {
        var rehearsal = ContainerService.RehearsalName("web");
        var runner = new FakeWslcRunner()
            .Answer(Inspect, FakeWslcRunner.Fixture("container-inspect.json"))
            .Answer("container stop web", "")
            .Answer($"container rm --force {rehearsal}", "")
            .Fail($"container run --detach --name {rehearsal} --entrypoint tini nginx -s --", "exec: \"tini\": executable file not found in $PATH")
            .Answer("container start web", "");
        var client = factory.ClientWith(runner);

        var response = await client.PostAsJsonAsync("/api/v1/containers/web/recreate", new ContainerLaunchRequest { Image = "nginx", Name = "web", Entrypoint = "tini -s --" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("Recreate aborted", problem!.Detail);
        Assert.Contains("web was not changed", problem.Detail);
        Assert.DoesNotContain(runner.Calls, call => call.SequenceEqual(new[] { "container", "rm", "--force", "web" }));
        Assert.Equal(["container", "start", "web"], runner.Calls[^1]);
    }

    /// <summary>The check before a launch reads the machine: a name another container has, a network that is not there; nothing is run.</summary>
    [Fact]
    public async Task Launch_check_reads_names_and_networks_from_the_machine()
    {
        var runner = new FakeWslcRunner()
            .Answer("container list --all --format json", FakeWslcRunner.Fixture("container-list.ndjson"))
            .Answer("container stats --all --format json", FakeWslcRunner.Fixture("container-stats.ndjson"))
            .Answer("network list --format json", FakeWslcRunner.Fixture("network-list.ndjson"))
            .Answer("network inspect bridge --format json", FakeWslcRunner.Fixture("network-inspect-bridge.json"))
            .Answer("network inspect host --format json", "[]")
            .Answer("network inspect none --format json", "[]")
            .Answer("network inspect appnet --format json", "[]")
            .Answer("image list --digests --format json", FakeWslcRunner.Fixture("image-list.ndjson"));
        var client = factory.ClientWith(runner);
        var request = new ContainerLaunchRequest { Image = "nginx", Name = "agent-zero", Network = "no-such-net", Publish = ["8080:80"], Command = "serve --port 3000" };

        var response = await client.PostAsJsonAsync("/api/v1/containers/launch-check", new LaunchCheckRequest(request));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var check = await response.Content.ReadFromJsonAsync<LaunchCheck>();
        Assert.NotNull(check);
        Assert.Contains(check.Errors, e => e.Field == LaunchFields.Name && e.Message.Contains("'agent-zero' is already the name"));
        Assert.Contains(check.Errors, e => e.Field == LaunchFields.Networks && e.Message.Contains("'no-such-net' does not exist"));
        Assert.Contains(check.Warnings, w => w.Field == LaunchFields.Command && w.Message.Contains("'--port 3000'"));
        Assert.DoesNotContain(runner.Calls, call => call.Count > 1 && call[1] is "run" or "create");
    }

    /// <summary>The check does not count the container being recreated against itself.</summary>
    [Fact]
    public async Task Launch_check_lets_a_recreate_keep_its_own_name()
    {
        var runner = new FakeWslcRunner()
            .Answer("container list --all --format json", FakeWslcRunner.Fixture("container-list.ndjson"))
            .Answer("container stats --all --format json", FakeWslcRunner.Fixture("container-stats.ndjson"))
            .Answer("image list --digests --format json", FakeWslcRunner.Fixture("image-list.ndjson"));
        var client = factory.ClientWith(runner);

        var response = await client.PostAsJsonAsync("/api/v1/containers/launch-check", new LaunchCheckRequest(new ContainerLaunchRequest { Image = "nginx", Name = "agent-zero" }, "agent-zero"));

        var check = await response.Content.ReadFromJsonAsync<LaunchCheck>();
        Assert.NotNull(check);
        Assert.Empty(check.Errors);
    }

    /// <summary>The UI removes with force: a running container goes without a stop first.</summary>
    [Fact]
    public async Task Remove_forces_only_when_asked()
    {
        var runner = new FakeWslcRunner()
            .Answer("container rm web", "")
            .Answer("container rm --force web", "");
        var client = factory.ClientWith(runner);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/v1/containers/web")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/v1/containers/web?force=true")).StatusCode);
        Assert.Equal(["container", "rm", "web"], runner.Calls[0]);
        Assert.Equal(["container", "rm", "--force", "web"], runner.Calls[1]);
    }

    [Fact]
    public async Task Details_and_logs_read_the_container()
    {
        // A container the registry has not numbered yet is entered by a read of
        // the list, which details makes: the list holds web, under its short id.
        var runner = new FakeWslcRunner()
            .Answer(Inspect, FakeWslcRunner.Fixture("container-inspect.json"))
            .Answer("container list --all --format json", "{\"ID\":\"5b5598e8c4fd\",\"Names\":\"web\",\"Image\":\"agent0ai/agent-zero:latest\",\"State\":\"running\",\"Labels\":\"\",\"Ports\":\"\"}\n")
            .Answer("container stats --all --format json", "")
            .Answer("container logs --tail 50 --timestamps web", "line 1\nline 2\n", stderr: "started\n");
        var client = factory.ClientWith(runner);

        var details = await client.GetFromJsonAsync<ContainerDetails>("/api/v1/containers/web/details");
        var logs = await client.GetFromJsonAsync<ContainerLogs>("/api/v1/containers/web/logs?tail=50&timestamps=true");

        Assert.NotNull(details);
        Assert.True(details.Uid > 0, "the details carry the container's number in the registry");
        Assert.Equal("web", details.Name);
        Assert.Equal("agent0ai/agent-zero:latest", details.Form.Image);
        Assert.Equal("no", details.RestartPolicy.Policy);
        Assert.Contains("\"Name\": \"/web\"", details.Inspect);
        Assert.Equal("started\nline 1\nline 2\n", logs!.Text);
    }

    [Fact]
    public async Task Restart_policy_can_be_set_and_cleared()
    {
        var runner = new FakeWslcRunner().Answer(Inspect, FakeWslcRunner.Fixture("container-inspect.json"));
        var client = factory.ClientWith(runner);

        var set = await client.PutAsJsonAsync("/api/v1/containers/web/restart-policy", new SetRestartPolicyRequest("unless-stopped"));
        var enrolled = await set.Content.ReadFromJsonAsync<RestartPolicyInfo>();
        var cleared = await client.PutAsJsonAsync("/api/v1/containers/web/restart-policy", new SetRestartPolicyRequest("no"));
        var none = await cleared.Content.ReadFromJsonAsync<RestartPolicyInfo>();

        Assert.Equal(new RestartPolicyInfo("unless-stopped", "running", true), enrolled);
        Assert.False(none!.Enrolled);
    }

    /// <summary>The cross of a failed run: forgetting one the agent no longer has is not an error, the row is gone either way.</summary>
    [Fact]
    public async Task Dismissing_a_run_that_is_gone_answers_no_content()
    {
        var response = await factory.ClientWith(new FakeWslcRunner()).DeleteAsync("/api/v1/containers/launches/no-such-run");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Version_reports_agent_and_wslc_versions()
    {
        var runner = new FakeWslcRunner().Answer("version --format json", "{\"Client\":{\"Version\":\"2.9.11.0\"}}");
        var client = factory.ClientWith(runner);

        var version = await client.GetFromJsonAsync<VersionResponse>("/api/v1/version");

        Assert.NotNull(version);
        Assert.Equal("2.9.11.0", version.Wslc);
        Assert.False(string.IsNullOrWhiteSpace(version.Agent));
    }
}
