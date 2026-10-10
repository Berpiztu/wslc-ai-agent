using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

public sealed class ContainerLaunchTests
{
    /// <summary>
    /// Copy run command and the Fill bar are the two ends of one contract: what
    /// the first writes, the second has to read back whole. The copy used to
    /// carry the name, the ports and the image alone, so pasting it created a
    /// container that was not the one copied.
    /// </summary>
    [Fact]
    public void A_copied_run_command_fills_the_form_back()
    {
        var request = new ContainerLaunchRequest
        {
            Image = "ghcr.io/open-webui/open-webui:main",
            Name = "open-webui--2",
            Command = "python app.py --port 8080",
            Entrypoint = "/bin/sh",
            Memory = "512m",
            Cpus = "1.5",
            Publish = ["3000:8080", "127.0.0.1:9000:9000"],
            Volumes = ["open-webui:/app/backend/data", @"c:\data\app:/a0/usr:ro"],
            Workdir = "/app",
            Env = ["KEY=value", "OTHER=a b"],
            Network = "appnet",
            Ip = "172.19.0.5",
            NetworkAliases = ["web", "front"],
            ConnectNetworks = ["backnet"],
            User = "1000",
            RestartPolicy = "always",
            StopTimeout = "30",
            HealthCmd = "curl -f http://localhost:8080 || exit 1",
            HealthInterval = "30s",
            HealthTimeout = "5s",
            HealthRetries = "3",
            HealthStartPeriod = "10s",
            PublicNames = ["8080:open-webui"],
        };

        // Full variables: the agent's own flags travel too, so nothing is left out.
        var parse = RunCommandLine.Parse(RunCommandLine.Write(request, fullVariables: true));

        Assert.Empty(parse.Unsupported);
        Assert.NotNull(parse.Request);
        // The record compares its lists by reference, so the two are compared by
        // what they write: the same line means the same container.
        Assert.Equal(RunCommandLine.Write(request, fullVariables: true), RunCommandLine.Write(parse.Request, fullVariables: true));
        Assert.Equal(request.Publish, parse.Request.Publish);
        Assert.Equal(request.Volumes, parse.Request.Volumes);
        Assert.Equal(request.Env, parse.Request.Env);
        Assert.Equal(request.NetworkAliases, parse.Request.NetworkAliases);
        Assert.Equal(request.ConnectNetworks, parse.Request.ConnectNetworks);
        Assert.Equal(request.Command, parse.Request.Command);
        Assert.Equal(request.RestartPolicy, parse.Request.RestartPolicy);
        Assert.Equal(request.PublicNames, parse.Request.PublicNames);
        Assert.Equal(request.HealthCmd, parse.Request.HealthCmd);
        // Without full variables the line is wslc's own: no agent flag in it.
        Assert.DoesNotContain("--restart", RunCommandLine.Write(request));
        Assert.DoesNotContain("--public-name", RunCommandLine.Write(request));
    }

    /// <summary>No healthcheck is a switch, and it replaces every health field.</summary>
    [Fact]
    public void A_container_without_a_healthcheck_says_so()
    {
        var request = new ContainerLaunchRequest { Image = "alpine:latest", NoHealthcheck = true };

        var line = RunCommandLine.Write(request);
        var parse = RunCommandLine.Parse(line);

        Assert.Contains("--no-healthcheck", line);
        Assert.NotNull(parse.Request);
        Assert.True(parse.Request.NoHealthcheck);
        Assert.Equal("", parse.Request.HealthCmd);
    }

    /// <summary>
    /// The GPU survives every way back to a launch: the line, the arguments and
    /// the metadata label a recreate reads it from. Lost once, a speech model
    /// came back without it and failed on start with no libcuda.
    /// </summary>
    [Fact]
    public void A_container_with_the_gpus_keeps_them()
    {
        var request = new ContainerLaunchRequest { Image = "ia-voice:latest", Gpus = true };

        var parse = RunCommandLine.Parse(RunCommandLine.Write(request));

        Assert.True(parse.Request!.Gpus);
        Assert.Empty(parse.Unsupported);
        Assert.Contains("--gpus all", string.Join(' ', LaunchArgs.Run(request)));
        Assert.True(ContainerService.HasGpus("""a=b,com.microsoft.wsl.container.metadata={"V1":{"Flags":2,"Ports":[]}}"""));
        Assert.False(ContainerService.HasGpus("""com.microsoft.wsl.container.metadata={"V1":{"Flags":32,"Ports":[]}}"""));
    }

    [Fact]
    public void Run_arguments_follow_a_fixed_order()
    {
        var request = new ContainerLaunchRequest
        {
            Image = "nginx:1.25",
            Name = "web",
            Memory = "512m",
            Cpus = "1.5",
            Publish = ["8080:80", "8443:443"],
            Volumes = ["C:\\data:/data:ro"],
            Workdir = "app",
            Env = ["A=1"],
            Entrypoint = "/bin/sh",
            Network = "appnet",
            Ip = "172.28.0.5",
            NetworkAliases = ["api"],
            User = "1000",
            StopTimeout = "0",
            HealthCmd = "curl -f http://localhost/",
            HealthRetries = "3",
            Command = "python app.py --port 80",
        };

        var args = LaunchArgs.Run(request);

        Assert.Equal(
            ["container", "run", "--detach", "--name", "web", "--memory", "512m", "--cpus", "1.5", "--publish", "8080:80", "--publish", "8443:443",
             "--volume", "C:\\data:/data:ro", "--workdir", "/app", "--env", "A=1", "--entrypoint", "/bin/sh", "--network", "appnet", "--ip", "172.28.0.5",
             "--network-alias", "api", "--user", "1000", "--stop-timeout", "0", "--health-cmd", "curl -f http://localhost/", "--health-retries", "3",
             "nginx:1.25", "python", "app.py", "--port", "80"],
            args);
    }

    /// <summary>
    /// <c>--entrypoint</c> takes one executable. An image's ENTRYPOINT ["tini",
    /// "-s", "--"] comes back from inspect as one field, and passed whole it
    /// asked runc for an executable called "tini -s --": the words after the
    /// first are its arguments and go before the command.
    /// </summary>
    [Fact]
    public void An_entrypoint_with_arguments_names_the_executable_and_puts_its_arguments_before_the_command()
    {
        var args = LaunchArgs.Run(new ContainerLaunchRequest { Image = "ghcr.io/openclaw/openclaw:latest", Entrypoint = "tini -s --", Command = "node dist/index.js gateway" });

        Assert.Equal(["container", "run", "--detach", "--entrypoint", "tini", "ghcr.io/openclaw/openclaw:latest", "-s", "--", "node", "dist/index.js", "gateway"], args);
    }

    [Fact]
    public void A_host_workdir_becomes_a_workspace_bind_and_the_ip_is_dropped_on_bridge()
    {
        var args = LaunchArgs.Create(new ContainerLaunchRequest { Image = "alpine", Workdir = "C:\\projects\\app", Network = "bridge", Ip = "172.17.0.9", NoHealthcheck = true, HealthCmd = "ignored" });

        Assert.Equal(["container", "create", "--volume", "C:\\projects\\app:/workspace", "--workdir", "/workspace", "--network", "bridge", "--no-healthcheck", "alpine"], args);
    }

    [Fact]
    public void Inspect_maps_the_launch_fields_back()
    {
        var result = new WslcResult(["container", "inspect"], 0, FakeWslcRunner.Fixture("container-inspect.json"), "", TimeSpan.Zero);

        var inspection = ContainerInspection.Parse(result);

        Assert.Equal("5b5598e8c4fd", inspection.Id);
        Assert.Equal("web", inspection.Name);
        Assert.Equal("agent0ai/agent-zero:latest", inspection.Image);
        Assert.True(inspection.IsRunning);
        Assert.Equal(["8085->80"], inspection.Ports);
        Assert.Equal(2, inspection.Mounts.Count);
        Assert.Equal(new MountInfo("bind", "C:\\data", "/data", "ro"), inspection.Mounts[1]);
        var form = inspection.Form;
        Assert.Equal("python app.py", form.Command);
        Assert.Equal("512m", form.Memory);
        Assert.Equal("1.5", form.Cpus);
        Assert.Equal(["127.0.0.1:8085:80"], form.Publish);
        Assert.Equal(["a0_usr:/a0/usr", "C:\\data:/data:ro"], form.Volumes);
        Assert.Equal("/a0", form.Workdir);
        Assert.Equal("appnet", form.Network);
        Assert.Equal("172.28.0.5", form.Ip);
        Assert.Equal(["api"], form.NetworkAliases);
        Assert.Equal(["bridge"], form.ConnectNetworks);
        Assert.Equal("10", form.StopTimeout);
        Assert.Equal("curl -f http://localhost/", form.HealthCmd);
        Assert.Equal("30s", form.HealthInterval);
        Assert.Equal("5s", form.HealthTimeout);
        Assert.Equal("10s", form.HealthStartPeriod);
        Assert.Equal("3", form.HealthRetries);
        Assert.False(form.NoHealthcheck);
    }

    /// <summary>
    /// View &amp; edit, then Save with nothing changed, has to launch the same
    /// container. Run left the entrypoint empty (the image's own), so it worked;
    /// the form read back from inspect carries that entrypoint spelled out, and
    /// every save from then on passed it as one word and failed.
    /// </summary>
    [Fact]
    public void A_form_read_from_inspect_and_saved_unchanged_launches_the_same_container()
    {
        const string inspect = """
            [{"Name":"/openclaw-gateway","Id":"ecd63c84fd1e9995e26402e18f72e434b89d33b6db68c380285299e5bdf66ab4",
              "Config":{"Image":"ghcr.io/openclaw/openclaw:latest","Entrypoint":["tini","-s","--"],"Cmd":["node","dist/index.js","gateway","--bind","lan"],
                        "User":"node","WorkingDir":"/app","Env":["NODE_ENV=production"],"Healthcheck":{"Test":["CMD","node","dist/docker-healthcheck.js"],"Interval":180000000000}},
              "HostConfig":{"NetworkMode":"bridge","PortBindings":{"18789/tcp":[{"HostIp":"127.0.0.1","HostPort":"28789"}]}},
              "Mounts":[{"Type":"volume","Name":"openclaw-state","Destination":"/home/node/.openclaw","RW":true}],
              "State":{"Status":"running","Running":true}}]
            """;
        var form = ContainerInspection.Parse(new WslcResult(["container", "inspect"], 0, inspect, "", TimeSpan.Zero)).Form;

        var args = LaunchArgs.Run(form);

        Assert.Equal("tini -s --", form.Entrypoint);
        Assert.Equal(
            ["container", "run", "--detach", "--name", "openclaw-gateway", "--publish", "127.0.0.1:28789:18789", "--volume", "openclaw-state:/home/node/.openclaw",
             "--workdir", "/app", "--env", "NODE_ENV=production", "--entrypoint", "tini", "--network", "bridge", "--user", "node",
             "--health-cmd", "node dist/docker-healthcheck.js", "--health-interval", "180s",
             "ghcr.io/openclaw/openclaw:latest", "-s", "--", "node", "dist/index.js", "gateway", "--bind", "lan"],
            args);
    }

    /// <summary>A run line broken with PowerShell's backtick folds like one broken with bash's backslash.</summary>
    [Fact]
    public void A_powershell_run_line_folds_its_backtick_breaks()
    {
        var parse = RunCommandLine.Parse("wslc run -d --name web `\r\n  -p 85:80 `\r\n  -e KEY=value `\r\n  nginx `\r\n  -s -- serve");

        Assert.NotNull(parse.Request);
        Assert.Empty(parse.Unsupported);
        Assert.Equal("web", parse.Request.Name);
        Assert.Equal(["85:80"], parse.Request.Publish);
        Assert.Equal("nginx", parse.Request.Image);
        Assert.Equal("-s -- serve", parse.Request.Command);
    }

    [Fact]
    public void A_pasted_run_line_fills_the_form()
    {
        var parse = RunCommandLine.Parse("docker run -d --name web -p 85:80 -v c:\\data\\app:/a0/usr \\\n  -e KEY=value --network appnet --hostname h --label a=b --cap-add NET_ADMIN agent0ai/agent-zero python app.py");

        Assert.NotNull(parse.Request);
        Assert.Equal("agent0ai/agent-zero", parse.Request.Image);
        Assert.Equal("web", parse.Request.Name);
        Assert.Equal(["85:80"], parse.Request.Publish);
        Assert.Equal(["c:\\data\\app:/a0/usr"], parse.Request.Volumes);
        Assert.Equal(["KEY=value"], parse.Request.Env);
        Assert.Equal("appnet", parse.Request.Network);
        Assert.Equal("python app.py", parse.Request.Command);
        Assert.Equal("h", parse.Request.Hostname);
        Assert.Equal(["a=b"], parse.Request.Labels);
        Assert.Equal(["--cap-add NET_ADMIN"], parse.Unsupported);
        Assert.StartsWith("Read as a wslc run. Filled: image agent0ai/agent-zero, name web, 1 port(s)", parse.Summary);
        Assert.Equal("No image reference found.", RunCommandLine.Parse("wslc run -d").Error);
    }

    [Fact]
    public void Fill_names_what_it_cannot_use_with_its_value_as_the_reference()
    {
        var parse = RunCommandLine.Parse("docker run -d -it -p 3000:8080 --add-host=host.docker.internal:host-gateway -v open-webui:/app/backend/data --name open-webui --restart always --pull missing ghcr.io/open-webui/open-webui:main");

        Assert.NotNull(parse.Request);
        Assert.Equal("always", parse.Request.RestartPolicy);
        Assert.Equal(["-it", "--pull missing"], parse.Unsupported);
        Assert.Equal(["--add-host=host.docker.internal:host-gateway"], parse.NotNeeded);
        Assert.Equal(
            "Read as a wslc run. Filled: image ghcr.io/open-webui/open-webui:main, name open-webui, 1 port(s), 1 volume(s). Not needed here, WSLC containers reach the host by that name already: --add-host=host.docker.internal:host-gateway. Not supported here: -it, --pull missing.",
            parse.Summary);
        Assert.Equal(["--add-host db:10.0.0.5"], RunCommandLine.Parse("docker run --add-host db:10.0.0.5 --add-host host.wslc.internal:host-gateway alpine").Unsupported);
        Assert.Equal("Paste a command first.", RunCommandLine.Parse("  ").Summary);
    }

    [Fact]
    public void Shell_words_split_and_join_round_trip()
    {
        Assert.Equal(["sh", "-c", "echo 'hi there'"], ShellWords.Split("sh -c \"echo 'hi there'\""));
        Assert.Equal("sh -c 'echo hi'", ShellWords.Join(["sh", "-c", "echo hi"]));
    }

    [Fact]
    public void A_multi_value_line_becomes_one_value_per_port_or_variable()
    {
        Assert.Equal(["8080:80", "127.0.0.1:18643:8643"], ValueList.Split("8080:80, 127.0.0.1:18643:8643"));
        Assert.Equal(["A=1", "B=2"], ValueList.SplitPairs("A=1, B=2"));
        Assert.Equal(["A=1", "B=2"], ValueList.SplitPairs("A=1\nB=2"));
    }

    [Fact]
    public void A_comma_inside_a_value_stays_part_of_it()
    {
        Assert.Equal(["OPTS=a,b"], ValueList.SplitPairs("OPTS=a,b"));
        Assert.Equal(["MSG=hello, world"], ValueList.SplitPairs("MSG=hello, world"));
    }

    [Fact]
    public void The_new_container_id_is_the_last_id_like_line()
    {
        Assert.Equal("5b5598e8c4fd", ContainerService.ContainerIdFrom("pulling...\n5b5598e8c4fd7a59ee04c1a96fec4a7c858668512c3a0605393b8110e24e0b1f\n"));
        Assert.Equal("web", ContainerService.ContainerIdFrom("web\n"));
    }
}
