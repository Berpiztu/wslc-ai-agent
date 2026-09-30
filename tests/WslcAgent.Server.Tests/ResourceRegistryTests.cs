using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WslcAgent.Server.Resources;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

/// <summary>A card on the dashboard points at a uid: it must survive a rename and a recreate, and go with a removal.</summary>
public sealed class ResourceRegistryTests
{
    private const string Kind = ResourceRegistry.Container;

    private static ResourceRegistry Registry() => Registry(out _);

    private static ResourceRegistry Registry(out ISelectedSession session)
    {
        var options = Options.Create(new WslcOptions { DataDirectory = TestHost.TempDataDirectory() });
        session = new SelectedSession(options);
        return new ResourceRegistry(options, session, NullLogger<ResourceRegistry>.Instance);
    }

    [Fact]
    public void An_image_is_its_tag_and_a_tag_that_starts_like_another_is_another()
    {
        var registry = Registry();
        var uids = registry.Reconcile(ResourceRegistry.Image, [("nginx:latest", "nginx:latest"), ("nginx:latest-alpine", "nginx:latest-alpine")], complete: true);

        Assert.NotEqual(uids["nginx:latest"], uids["nginx:latest-alpine"]);
        Assert.Equal(uids["nginx:latest"], registry.Reconcile(ResourceRegistry.Image, [("nginx:latest", "nginx:latest")], complete: true)["nginx:latest"]);
        Assert.Equal(0, registry.UidOf(ResourceRegistry.Image, "nginx:latest-alpine", "nginx:latest-alpine"));
    }

    [Fact]
    public void Another_session_does_not_take_this_ones_uids()
    {
        var registry = Registry(out var session);
        var uid = registry.Reconcile(Kind, [("aaa111", "web")], complete: true)["aaa111"];

        session.Name = "other";
        registry.Reconcile(Kind, [("zzz999", "db")], complete: true);
        session.Name = "";

        Assert.Equal(uid, registry.UidOf(Kind, "aaa111", "web"));
    }

    [Fact]
    public void A_rename_keeps_the_uid()
    {
        var registry = Registry();
        var uid = registry.Reconcile(Kind, [("aaa111", "web")], complete: true)["aaa111"];

        Assert.Equal(uid, registry.Reconcile(Kind, [("aaa111", "site")], complete: true)["aaa111"]);
    }

    [Fact]
    public void A_recreate_behind_the_agents_back_keeps_the_uid()
    {
        var registry = Registry();
        var uid = registry.Reconcile(Kind, [("aaa111", "web")], complete: true)["aaa111"];

        Assert.Equal(uid, registry.Reconcile(Kind, [("bbb222", "web")], complete: true)["bbb222"]);
    }

    [Fact]
    public void A_removal_takes_the_uid_with_it()
    {
        var registry = Registry();
        var uid = registry.Reconcile(Kind, [("aaa111", "web")], complete: true)["aaa111"];

        registry.Reconcile(Kind, [], complete: true);

        Assert.NotEqual(uid, registry.Reconcile(Kind, [("ccc333", "other")], complete: true)["ccc333"]);
        Assert.Equal(0, registry.UidOf(Kind, "aaa111", "web"));
    }

    [Fact]
    public void The_agents_own_recreate_keeps_the_uid_through_a_new_name()
    {
        var registry = Registry();
        var uid = registry.Reconcile(Kind, [("aaa111", "web")], complete: true)["aaa111"];

        registry.Hold("aaa111");
        // A read in the middle: the old one is gone, the new one already there under its new name.
        registry.Reconcile(Kind, [("bbb222", "site")], complete: true);
        registry.Recreated(Kind, "aaa111", "bbb222", "site");
        registry.Release("aaa111");

        Assert.Equal(uid, registry.Reconcile(Kind, [("bbb222", "site")], complete: true)["bbb222"]);
    }
}
