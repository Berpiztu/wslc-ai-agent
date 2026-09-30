using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Tests;

/// <summary>Stats, the inspect JSON download, the form read from a JSON, backups and image inspect.</summary>
public sealed class ContainerExtrasEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Inspect = "container inspect web --format json";

    [Fact]
    public async Task Stats_reads_the_row_back_into_numbers()
    {
        var runner = new FakeWslcRunner().Answer("container stats agent-zero --format json",
            """{"BlockIO":"1.2MB / 0B","CPUPerc":"0.40%","ID":"5b5598e8c4fd7a59ee04c1a96fec4a7c858668512c3a0605393b8110e24e0b1f","MemPerc":"11.30%","MemUsage":"885.1MiB / 7.6GiB","Name":"agent-zero","NetIO":"3.1kB / 2.2kB","PIDs":12}""");
        var client = factory.ClientWith(runner);

        var stats = await client.GetFromJsonAsync<ContainerStats>("/api/v1/containers/agent-zero/stats");

        Assert.NotNull(stats);
        Assert.Equal("5b5598e8c4fd", stats.Id);
        Assert.Equal(0.4, stats.CpuPercent);
        Assert.Equal(928_094_618, stats.MemoryUsedBytes);
        Assert.Equal(1_200_000, stats.DiskReadBytes);
        Assert.Equal(2_200, stats.NetworkSentBytes);
        Assert.Equal(12, stats.Pids);
    }

    [Fact]
    public async Task Inspect_json_downloads_under_the_container_name()
    {
        var runner = new FakeWslcRunner().Answer(Inspect, FakeWslcRunner.Fixture("container-inspect.json"));
        var client = factory.ClientWith(runner);

        var response = await client.GetAsync("/api/v1/containers/web/inspect.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("web-inspect.json", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Contains("\"Name\": \"/web\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Launch_form_is_read_from_an_inspect_output_or_an_exported_request()
    {
        var client = factory.ClientWith(new FakeWslcRunner());

        var fromInspect = await client.PostAsJsonAsync("/api/v1/containers/launch-form", new LaunchFormSource(FakeWslcRunner.Fixture("container-inspect.json")));
        var form = await fromInspect.Content.ReadFromJsonAsync<ContainerLaunchRequest>();
        var fromRequest = await client.PostAsJsonAsync("/api/v1/containers/launch-form", new LaunchFormSource("""{"image":"alpine:latest","name":"a1"}"""));
        var request = await fromRequest.Content.ReadFromJsonAsync<ContainerLaunchRequest>();
        var neither = await client.PostAsJsonAsync("/api/v1/containers/launch-form", new LaunchFormSource("""{"hello":1}"""));

        Assert.Equal("agent0ai/agent-zero:latest", form!.Image);
        Assert.Equal("a1", request!.Name);
        Assert.Equal(HttpStatusCode.BadRequest, neither.StatusCode);
    }

    [Fact]
    public async Task Backup_refuses_a_running_container_at_the_check_step()
    {
        var runner = new FakeWslcRunner().Answer(Inspect, FakeWslcRunner.Fixture("container-inspect.json"));
        var client = factory.ClientWith(runner);

        var started = await client.PostAsync("/api/v1/containers/web/backup", content: null);
        var job = await started.Content.ReadFromJsonAsync<BackupJob>();
        var polled = await client.GetFromJsonAsync<BackupJob>($"/api/v1/containers/backups/{job!.Id}");
        var download = await client.GetAsync($"/api/v1/containers/backups/{job.Id}/download");
        var unknown = await client.GetAsync("/api/v1/containers/backups/nope");

        Assert.Equal(BackupState.Error, job.State);
        Assert.Equal(BackupStep.Check, job.Step);
        Assert.Equal("Container web is running. Stop it before backing it up.", polled!.Error);
        Assert.Equal(HttpStatusCode.Conflict, download.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.DoesNotContain(runner.Calls, call => call.Contains("export"));
    }

    [Fact]
    public async Task Exec_runs_the_command_split_like_a_shell()
    {
        var runner = new FakeWslcRunner().Answer("exec web ls -la /app", "total 0\n");
        var client = factory.ClientWith(runner);

        var response = await client.PostAsJsonAsync("/api/v1/containers/web/exec", new ContainerExecRequest("ls -la /app"));
        var result = await response.Content.ReadFromJsonAsync<ContainerExecResult>();

        Assert.Equal(["exec", "web", "ls", "-la", "/app"], runner.Calls.Single());
        Assert.Equal("total 0\n", result!.Stdout);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Exec_reports_the_command_failure_as_its_own_answer()
    {
        var runner = new FakeWslcRunner().Fail("exec web grep nope /etc/hosts", "", exitCode: 1);
        var client = factory.ClientWith(runner);

        var response = await client.PostAsJsonAsync("/api/v1/containers/web/exec", new ContainerExecRequest("grep nope /etc/hosts"));
        var result = await response.Content.ReadFromJsonAsync<ContainerExecResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, result!.ExitCode);
    }

    [Fact]
    public async Task Open_terminal_is_refused_for_a_client_that_is_not_the_agent_machine()
    {
        var agent = factory.Agent(new FakeWslcRunner());
        var token = (await (await agent.CreateClient().PostAsync("/api/v1/login/api-token", null)).Content.ReadFromJsonAsync<ApiTokenResult>())!.Token;
        var remote = agent.CreateClient();
        remote.DefaultRequestHeaders.Add(TestHost.RemoteHeader, "1");
        remote.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await remote.PostAsJsonAsync("/api/v1/containers/web/open-terminal", new OpenTerminalRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Files_lists_a_directory_with_folders_first()
    {
        var runner = new FakeWslcRunner()
            .Answer("exec web ls -la -- /app/.", """
                total 12
                drwxr-xr-x 2 root root 4096 Sep 15 08:55 .
                drwxr-xr-x 1 root root 4096 Sep 15 08:54 ..
                -rw-r--r-- 1 root root  220 Sep 15 08:55 app.py
                drwxr-xr-x 2 root root 4096 Sep 15 08:55 static
                lrwxrwxrwx 1 root root    7 Sep 15 08:55 link -> app.py
                """);
        var client = factory.ClientWith(runner);

        var listing = await client.GetFromJsonAsync<ContainerFileListing>("/api/v1/containers/web/files?path=/app");

        Assert.NotNull(listing);
        Assert.Equal("/app", listing.Path);
        Assert.Equal("/", listing.Parent);
        Assert.Equal(["static", "link", "app.py"], listing.Entries.Select(e => e.Name));
        Assert.Equal("/app/app.py", listing.Entries[2].Path);
        Assert.Equal(220, listing.Entries[2].Size);
        Assert.Equal("app.py", listing.Entries[1].Target);
        Assert.DoesNotContain(runner.Calls, call => call.Contains("test"));
    }

    [Fact]
    public async Task Files_of_a_stopped_container_say_so_instead_of_blaming_the_path()
    {
        var runner = new FakeWslcRunner()
            .Fail("exec web ls -la -- /app/.", "WSLC_E_CONTAINER_NOT_RUNNING: web");
        var client = factory.ClientWith(runner);

        var response = await client.GetAsync("/api/v1/containers/web/files?path=/app");
        var problem = await response.Content.ReadAsStringAsync();

        Assert.Contains("not running", problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Files_paths_are_cleaned_before_they_reach_a_command()
    {
        var runner = new FakeWslcRunner().Answer("exec web rm -rf -- /app/data", "");
        var client = factory.ClientWith(runner);

        var response = await client.DeleteAsync("/api/v1/containers/web/files?path=/app/../app/./data/");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["exec", "web", "rm", "-rf", "--", "/app/data"], runner.Calls.Single());
    }

    [Fact]
    public async Task Image_inspect_returns_the_indented_json()
    {
        var runner = new FakeWslcRunner().Answer("image inspect alpine:latest --format json", """[{"Id":"sha256:abc","RepoTags":["alpine:latest"]}]""");
        var client = factory.ClientWith(runner);

        var inspect = await client.GetFromJsonAsync<ImageInspect>("/api/v1/images/inspect?reference=alpine:latest");

        Assert.NotNull(inspect);
        Assert.Equal("alpine:latest", inspect.Reference);
        Assert.Contains("\"Id\": \"sha256:abc\"", inspect.Json);
    }
}
