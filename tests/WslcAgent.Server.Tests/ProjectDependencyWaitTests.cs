using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Projects;

namespace WslcAgent.Server.Tests;

/// <summary>
/// What a service waits for (<c>service_completed_successfully</c>, <c>service_healthy</c>)
/// has no time limit, as in Compose: a dependency that downloads a model for an hour is still
/// on its way, and a limit made the project fail while it worked. The wait ends when what it
/// waits for has come, or cannot come any more.
/// </summary>
public sealed class ProjectDependencyWaitTests
{
    [Fact]
    public void A_dependency_still_running_is_waited_for_however_long()
    {
        Assert.False(ProjectRunner.DependencyMet(Row("running", "Up 3 hours"), healthy: false, healthOverdue: true, "batch"));
        Assert.False(ProjectRunner.DependencyMet(Row("running", "Up 3 hours", health: "starting"), healthy: true, healthOverdue: true, "batch"));
    }

    [Fact]
    public void A_dependency_not_made_or_not_started_yet_is_waited_for()
    {
        Assert.False(ProjectRunner.DependencyMet(null, healthy: false, healthOverdue: false, "batch"));
        Assert.False(ProjectRunner.DependencyMet(Row("created", "Created"), healthy: false, healthOverdue: false, "batch"));
    }

    [Fact]
    public void A_dependency_that_ended_well_is_met()
    {
        Assert.True(ProjectRunner.DependencyMet(Row("exited", "Exited (0) 2 seconds ago"), healthy: false, healthOverdue: false, "batch"));
    }

    [Fact]
    public void A_dependency_that_ended_badly_stops_the_wait_with_its_exit_code()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ProjectRunner.DependencyMet(Row("exited", "Exited (137) 2 seconds ago"), healthy: false, healthOverdue: false, "batch"));

        Assert.Contains("exit code 137", error.Message);
        Assert.Contains("batch", error.Message);
    }

    [Fact]
    public void A_healthy_dependency_is_met()
    {
        Assert.True(ProjectRunner.DependencyMet(Row("running", "Up 1 minute", health: "healthy"), healthy: true, healthOverdue: true, "web"));
    }

    [Theory]
    [InlineData("running", "Up 5 minutes", "unhealthy", "unhealthy")]
    [InlineData("exited", "Exited (1) 2 seconds ago", "", "stopped before it was healthy")]
    public void A_dependency_that_cannot_become_healthy_stops_the_wait(string state, string status, string health, string why)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ProjectRunner.DependencyMet(Row(state, status, health), healthy: true, healthOverdue: false, "web"));

        Assert.Contains(why, error.Message);
    }

    [Fact]
    public void A_dependency_with_no_health_check_stops_the_wait_once_its_health_is_overdue()
    {
        var row = Row("running", "Up 1 minute");

        // Just started: its health may not show yet.
        Assert.False(ProjectRunner.DependencyMet(row, healthy: true, healthOverdue: false, "web"));
        var error = Assert.Throws<InvalidOperationException>(() => ProjectRunner.DependencyMet(row, healthy: true, healthOverdue: true, "web"));
        Assert.Contains("no health check", error.Message);
    }

    private static ContainerSummary Row(string state, string status, string health = "") =>
        new("id-prepare", "shop-prepare", "img", state, status, "", "", "", [], "", "", health, "");
}
