using Microsoft.Extensions.Logging.Abstractions;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

/// <summary>
/// The exec events of wslc 3.0.5, as it prints them, and which of them the
/// Recent events list shows: a command run from elsewhere, once, at its end,
/// without its command; never a health check's probe nor the agent's own.
/// </summary>
public sealed class ExecEventTests
{
    private const string Id = "8b884498cb80507fb4c25f0f7c2414a2e59f2862bb352c6fdb81b8536c95ee61";

    [Fact]
    public void A_create_of_one_line_gives_its_command_and_its_exec()
    {
        var read = ExecEvent.Parse($"2026-11-02T23:03:07.521322451-05:00 container exec_create: true  {Id} (execID=ab12, image=alpine:latest, name=web)");

        Assert.NotNull(read);
        Assert.Equal(ExecEvent.Create, read.Action);
        Assert.Equal(Id, read.ContainerId);
        Assert.Equal("ab12", read.ExecId);
        Assert.Equal("true", read.Command);
        Assert.Equal("web", read.Name);
    }

    [Fact]
    public void A_create_of_several_lines_keeps_them_all_and_its_labels_change_nothing()
    {
        var read = ExecEvent.Parse($"2026-11-02T23:07:41.411800854-05:00 container exec_create: sh -c true\ntrue {Id} (ai.berpiztu.wslc.project=ia-apps, execID=ab12, image=alpine:latest, name=web)");

        Assert.NotNull(read);
        Assert.Equal("sh -c true\ntrue", read.Command);
        Assert.Equal("ab12", read.ExecId);
        Assert.Equal("web", read.Name);
    }

    [Fact]
    public void A_die_gives_its_exit_code_and_no_command()
    {
        var read = ExecEvent.Parse($"2026-11-02T23:03:07.764345048-05:00 container exec_die {Id} (ai.berpiztu.wslc.project=ia-apps, execID=ab12, exitCode=7, image=alpine:latest, name=web)");

        Assert.NotNull(read);
        Assert.Equal(ExecEvent.Die, read.Action);
        Assert.Equal(7, read.ExitCode);
        Assert.Equal("", read.Command);
    }

    [Fact]
    public async Task A_command_run_from_elsewhere_is_listed_once_at_its_end()
    {
        var (watch, recent, _) = Watch(new FakeWslcRunner().Answer($"container inspect {Id}", $"[{{\"Id\":\"{Id}\",\"Config\":{{}}}}]"));

        await watch.Heard(Created("ab12", "cat /etc/passwd"));
        await watch.Heard(Died("ab12", 7));

        var listed = Assert.Single(recent.Recent(10));
        Assert.Equal(ExecWatch.Action, listed.Action);
        Assert.Equal(7, listed.ExitCode);
        Assert.Equal("web", listed.Name);
    }

    [Fact]
    public async Task A_health_check_probe_is_not_listed()
    {
        var inspect = $"[{{\"Id\":\"{Id}\",\"Config\":{{\"Healthcheck\":{{\"Test\":[\"CMD-SHELL\",\"curl -f http://127.0.0.1:8030/\"]}}}}}}]";
        var (watch, recent, _) = Watch(new FakeWslcRunner().Answer($"container inspect {Id}", inspect));

        await watch.Heard(Created("ab12", "/bin/sh -c curl -f http://127.0.0.1:8030/"));
        await watch.Heard(Died("ab12", 0));

        Assert.Empty(recent.Recent(10));
    }

    [Fact]
    public async Task The_agents_own_command_is_not_listed()
    {
        var (watch, recent, agent) = Watch(new FakeWslcRunner().Answer($"container inspect {Id}", $"[{{\"Id\":\"{Id}\",\"Config\":{{}}}}]"));

        agent.Starting(["exec", "web", "ls", "-la", "/"]);
        await watch.Heard(Created("ab12", "ls -la /"));
        await watch.Heard(Died("ab12", 0));

        Assert.Empty(recent.Recent(10));
    }

    private static (ExecWatch Watch, RecentWslcEvents Recent, AgentExecs Agent) Watch(FakeWslcRunner wslc)
    {
        var recent = new RecentWslcEvents();
        var agent = new AgentExecs(TimeProvider.System);
        return (new ExecWatch(wslc, agent, recent, NullLogger<ExecWatch>.Instance), recent, agent);
    }

    private static ExecEvent Created(string execId, string command) =>
        new(DateTimeOffset.Now, ExecEvent.Create, Id, execId, command, "web", null);

    private static ExecEvent Died(string execId, int exitCode) =>
        new(DateTimeOffset.Now, ExecEvent.Die, Id, execId, "", "web", exitCode);
}
