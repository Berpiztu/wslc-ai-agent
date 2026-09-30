using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Containers;

namespace WslcAgent.Server.Tests;

public sealed class ContainerRecreationsTests
{
    private static ContainerSummary Row(string id, string name, string state = "exited", int uid = 0) =>
        new(id, name, "alpine:latest", state, "Exited (0)", "sh", "", "", [], "bridge", "0B", "", "linux/amd64", Uid: uid);

    [Fact]
    public void A_container_being_recreated_keeps_one_row_under_its_uid_throughout()
    {
        var recreations = new ContainerRecreations();
        recreations.Overlay([Row("aaaaaaaaaaaa", "web", "running", 7), Row("bbbbbbbbbbbb", "db", uid: 8)], complete: true);
        recreations.Begin("aaaaaaaaaaaa0000", "web", uid: 7);

        var stillThere = recreations.Overlay([Row("aaaaaaaaaaaa", "web", uid: 7), Row("bbbbbbbbbbbb", "db", uid: 8)], complete: true);
        var gone = recreations.Overlay([Row("bbbbbbbbbbbb", "db", uid: 8)], complete: true);
        var replaced = recreations.Overlay([Row("cccccccccccc", "web", uid: 9), Row("bbbbbbbbbbbb", "db", uid: 8)], complete: true);

        foreach (var list in new[] { stillThere, gone, replaced })
        {
            var web = Assert.Single(list, row => row.Name == "web");
            Assert.Equal(ContainerRecreations.State, web.State);
            Assert.Equal(7, web.Uid);
            Assert.Equal("exited", Assert.Single(list, row => row.Name == "db").State);
        }

        recreations.End("aaaaaaaaaaaa0000");
        var after = recreations.Overlay([Row("cccccccccccc", "web", uid: 7), Row("bbbbbbbbbbbb", "db", uid: 8)], complete: true);
        Assert.Equal("exited", Assert.Single(after, row => row.Name == "web").State);
    }

    [Fact]
    public void A_list_of_the_running_ones_adds_no_stopped_row()
    {
        var recreations = new ContainerRecreations();
        recreations.Overlay([Row("aaaaaaaaaaaa", "web", uid: 7)], complete: true);
        recreations.Begin("aaaaaaaaaaaa", "web", uid: 7);

        Assert.Empty(recreations.Overlay([], complete: false));
    }
}
