using Microsoft.Extensions.Logging.Abstractions;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

public sealed class StoppedSessionsTests
{
    [Fact]
    public void A_stopped_session_refuses_what_would_open_it_until_it_is_started()
    {
        var stopped = new StoppedSessions(NullLogger<StoppedSessions>.Instance);
        stopped.Hold("dev", id: 7);

        // A screen's poll brought the session back a second after the terminate.
        Assert.NotNull(stopped.Refusal(["container", "stats", "--all"], "dev"));
        Assert.NotNull(stopped.Refusal(["events"], "dev"));
        // Measured to leave a stopped session down, so the panels still answer.
        Assert.Null(stopped.Refusal(["system", "info", "--format", "json"], "dev"));
        Assert.Null(stopped.Refusal(["system", "session", "list"], "dev"));
        Assert.Null(stopped.Refusal(["version"], "dev"));
        // Other sessions are not held.
        Assert.Null(stopped.Refusal(["container", "list"], "other"));

        stopped.Release("dev");

        Assert.Null(stopped.Refusal(["container", "stats", "--all"], "dev"));
    }

    [Fact]
    public void Only_a_new_session_opened_from_outside_lets_go()
    {
        var stopped = new StoppedSessions(NullLogger<StoppedSessions>.Instance);
        stopped.Hold("dev", id: 7);

        // The same ID is the session on its way down, still listed.
        stopped.Observe([("dev", 7)]);
        Assert.NotNull(stopped.Refusal(["container", "list"], "dev"));

        // A terminal on the machine opened it again: the user's word too.
        stopped.Observe([("dev", 8)]);
        Assert.Null(stopped.Refusal(["container", "list"], "dev"));
    }
}
