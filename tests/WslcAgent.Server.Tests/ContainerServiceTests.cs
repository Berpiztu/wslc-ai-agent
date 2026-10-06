using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Networks;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

public sealed class ContainerServiceTests
{
    private static FakeWslcRunner ScriptedAll() => new FakeWslcRunner()
        .Answer("container list --all --format json", FakeWslcRunner.Fixture("container-list.ndjson"))
        .Answer("container stats --all --format json", FakeWslcRunner.Fixture("container-stats.ndjson"));

    private static ContainerService Service(FakeWslcRunner runner)
    {
        var usage = new ContainerUsageScanner(runner, NullLogger<ContainerUsageScanner>.Instance);
        var options = Options.Create(new WslcOptions { DataDirectory = TestHost.TempDataDirectory() });
        var registry = new Resources.ResourceRegistry(options, new SelectedSession(options), NullLogger<Resources.ResourceRegistry>.Instance);
        var networks = new NetworkService(runner, usage, registry, NullLogger<NetworkService>.Instance);
        var policies = new RestartPolicyStore(options, NullLogger<RestartPolicyStore>.Instance);
        var publications = new Publishing.PublicationStore(options, NullLogger<Publishing.PublicationStore>.Instance);
        var restarter = new ContainerRestarter(runner, policies, NullLogger<ContainerRestarter>.Instance);
        var publishing = new Publishing.PublishingService(publications, new Publishing.PublishingSettingsStore(options), networks, new FakePublishedPlugin(), NullLogger<Publishing.PublishingService>.Instance);
        return new ContainerService(runner, networks, policies, publications, publishing, restarter, registry, new ContainerRecreations(), new ImageUpdatesUnderWay(), new WslcEvents(), NullLogger<ContainerService>.Instance);
    }

    [Fact]
    public async Task List_maps_the_ndjson_rows_wslc_prints()
    {
        var response = await Service(ScriptedAll()).ListAsync(all: true);

        Assert.Equal(2, response.Containers.Count);
        var first = response.Containers[0];
        Assert.Equal("8449a1cce27f", first.Id);
        Assert.Equal("jade_wasatch", first.Name);
        Assert.Equal("alpine:latest", first.Image);
        Assert.Equal("exited", first.State);
        Assert.False(first.IsRunning);
        Assert.Equal("sleep infinity", first.Command);
        Assert.Equal("linux/amd64", first.Platform);
        Assert.Equal(["8071->80"], first.Ports);
    }

    [Fact]
    public async Task List_merges_stats_by_id_prefix_and_aggregates_them()
    {
        var response = await Service(ScriptedAll()).ListAsync(all: true);

        var running = response.Containers[1];
        Assert.Equal("agent-zero", running.Name);
        Assert.Equal("0.40%", running.CpuPercent);
        Assert.Equal("885.1MiB / 7.6GiB", running.MemUsage);
        Assert.Equal("11.30%", running.MemPercent);
        Assert.Equal("1.2MB / 0B", running.DiskIo);
        Assert.Equal(0.4, response.Aggregate.CpuUsedPercent);
        Assert.Equal(StatsParsing.Bytes("885.1MiB"), response.Aggregate.MemoryUsedBytes);
        Assert.True(response.Aggregate.CpuCount >= 1);
    }

    [Fact]
    public async Task A_failing_stats_call_still_returns_the_list()
    {
        var runner = new FakeWslcRunner()
            .Answer("container list --all --format json", FakeWslcRunner.Fixture("container-list.ndjson"))
            .Fail("container stats --all --format json", "stats unavailable");

        var response = await Service(runner).ListAsync(all: true);

        Assert.Equal(2, response.Containers.Count);
        Assert.All(response.Containers, c => Assert.Equal("", c.CpuPercent));
    }

    [Fact]
    public async Task Helper_containers_are_hidden_unless_asked_for()
    {
        var helper = "{\"ID\":\"aaaaaaaaaaaa\",\"Names\":\"wslc-agent-files-1234\",\"Image\":\"alpine\",\"State\":\"running\",\"Labels\":\"\",\"Ports\":\"\"}\n";
        var runner = new FakeWslcRunner()
            .Answer("container list --all --format json", FakeWslcRunner.Fixture("container-list.ndjson") + helper)
            .Answer("container stats --all --format json", "");

        var hidden = await Service(runner).ListAsync(all: true);
        var shown = await Service(runner).ListAsync(all: true, helpers: true);

        Assert.Equal(2, hidden.Containers.Count);
        Assert.Equal(3, shown.Containers.Count);
    }

    [Fact]
    public async Task Running_only_omits_the_all_flag_on_both_commands()
    {
        var runner = new FakeWslcRunner()
            .Answer("container list --format json", "")
            .Answer("container stats --format json", "");

        var response = await Service(runner).ListAsync(all: false);

        Assert.Empty(response.Containers);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Contains(runner.Calls, c => c.SequenceEqual(["container", "list", "--format", "json"]));
        Assert.Contains(runner.Calls, c => c.SequenceEqual(["container", "stats", "--format", "json"]));
    }

    [Fact]
    public void Rendered_ports_win_over_the_label_and_read_as_host_to_container()
    {
        var ports = ContainerService.ParsePorts("0.0.0.0:8080->80/tcp, :::8080->80/tcp", "com.microsoft.wsl.container.metadata={\"V1\":{\"Ports\":[]}}");

        Assert.Equal(["8080->80"], ports);
    }

    [Theory]
    [InlineData("885.1MiB / 7.6GiB", 928_094_618L)]
    [InlineData("1.2MB / 0B", 1_200_000L)]
    [InlineData("0B / 0B", 0L)]
    [InlineData("—", 0L)]
    public void Stats_memory_usage_parses_the_used_part(string text, long bytes) =>
        Assert.Equal(bytes, StatsParsing.UsedBytes(text));

    [Fact]
    public void Json_rows_accept_an_array_as_well_as_lines()
    {
        Assert.Equal(2, WslcJson.ParseRows("{\"a\":1}\n{\"a\":2}\n").Count);
        Assert.Equal(2, WslcJson.ParseRows("[{\"a\":1},{\"a\":2}]").Count);
        Assert.Empty(WslcJson.ParseRows("   "));
    }

    [Fact]
    public void Json_rows_accept_one_object_indented_over_several_lines()
    {
        var rows = WslcJson.ParseRows("{\r\n  \"a\": {\r\n    \"b\": 1\r\n  }\r\n}\r\n");

        Assert.Equal(1, Assert.Single(rows).GetProperty("a").GetProperty("b").GetInt32());
    }

    [Fact]
    public void Launch_form_reads_the_indented_inspect_file_the_export_writes()
    {
        const string exported = """
            {
              "Config": {
                "Image": "nousresearch/hermes-agent:latest",
                "Env": ["HERMES_HOME=/opt/data"]
              },
              "HostConfig": { "Memory": 4294967296 },
              "Name": "/hermes"
            }
            """;

        var form = Service(new FakeWslcRunner()).LaunchFormFromJson(exported);

        Assert.Equal("nousresearch/hermes-agent:latest", form.Image);
        Assert.Equal("hermes", form.Name);
    }

    [Fact]
    public async Task Restart_is_one_command_where_wslc_has_the_verb()
    {
        var runner = new FakeWslcRunner().Answer("container restart web", "");

        await Service(runner).RestartAsync("web");

        Assert.Equal(["container", "restart", "web"], runner.Calls.Single());
    }

    [Fact]
    public async Task Restart_falls_back_to_stop_and_start_on_an_older_wslc()
    {
        var runner = new FakeWslcRunner()
            .Fail("container restart web", "Unrecognized command: restart")
            .Answer("container stop web", "")
            .Answer("container start web", "");

        await Service(runner).RestartAsync("web");

        Assert.Equal(
            ["container restart web", "container stop web", "container start web"],
            runner.Calls.Select(call => string.Join(' ', call)));
    }

    [Fact]
    public void Session_flag_is_added_except_where_it_must_not_be()
    {
        Assert.Equal(["--session", "dev", "container", "list"], WslcRunner.ApplySession(["container", "list"], "dev"));
        Assert.Equal(["container", "list"], WslcRunner.ApplySession(["container", "list"], ""));
        Assert.Equal(["system", "session", "list"], WslcRunner.ApplySession(["system", "session", "list"], "dev"));
        Assert.Equal(["--session", "x", "ps"], WslcRunner.ApplySession(["--session", "x", "ps"], "dev"));

        // Terminate without the flag takes down the default session, whichever
        // one was asked for: a stop that hits the wrong session is silent.
        Assert.Equal(
            ["--session", "dev", "system", "session", "terminate"],
            WslcRunner.ApplySession(["system", "session", "terminate"], "dev"));

        // The CLI's own store for this user is named by naming none: a command
        // with no --session is what opens it again once it has been stopped.
        Assert.Equal(["container", "list"], WslcRunner.ApplySession(["container", "list"], SessionStores.Default));
    }
}
