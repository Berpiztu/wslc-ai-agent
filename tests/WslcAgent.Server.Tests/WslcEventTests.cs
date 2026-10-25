using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

/// <summary>
/// The line `wslc events` prints, read the only way that holds: the four
/// fields before the bracket. What is inside the bracket is a container's
/// labels, and a label's value carries commas and brackets of its own.
/// </summary>
public sealed class WslcEventTests
{
    [Fact]
    public void An_event_is_its_first_four_fields()
    {
        var read = WslcEventParsing.Parse(
            "2026-09-22T19:33:11.000000000-05:00 container stop 8449a1cce27f93 (exitCode=137, image=alpine:latest, name=jade_wasatch)");

        Assert.NotNull(read);
        Assert.Equal("container", read.Type);
        Assert.Equal("stop", read.Action);
        Assert.Equal("8449a1cce27f93", read.Id);
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 19, 33, 11, TimeSpan.FromHours(-5)), read.Time);
        Assert.Equal(137, read.ExitCode);
        Assert.Equal("jade_wasatch", read.Name);
    }

    [Fact]
    public void A_label_with_commas_and_brackets_of_its_own_changes_nothing()
    {
        // The real line that makes splitting the attributes impossible.
        var read = WslcEventParsing.Parse(
            "2026-09-22T19:32:11.000000000-05:00 container start 5b5c2fad701c (image=ghcr.io/open-webui/open-webui:main, " +
            "org.opencontainers.image.description=User-friendly AI Interface (Supports Ollama, OpenAI API, ...), name=open-webui--2)");

        Assert.NotNull(read);
        Assert.Equal("container", read.Type);
        Assert.Equal("start", read.Action);
        Assert.Equal("5b5c2fad701c", read.Id);
        Assert.Null(read.ExitCode);
        // The name after a label whose own value has commas and brackets
        Assert.Equal("open-webui--2", read.Name);
    }

    [Fact]
    public void A_die_of_wslc_3_0_2_gives_its_exit_code_wherever_it_stands()
    {
        // From 3.0.2 the attributes come in alphabetical order, labels among them.
        var read = WslcEventParsing.Parse(
            "2026-10-07T09:12:40.123456789Z container die 8449a1cce27f93 (com.example.note=a, b, execDuration=12, exitCode=3, image=alpine:latest, name=jade_wasatch)");

        Assert.NotNull(read);
        Assert.Equal("die", read.Action);
        Assert.Equal("8449a1cce27f93", read.Id);
        // Nine fractional digits and a Z, as wslc 3.0.2 writes them.
        Assert.Equal(TimeSpan.Zero, read.Time.Offset);
        Assert.Equal(new DateTime(2026, 10, 7, 9, 12, 40), read.Time.DateTime.AddTicks(-(read.Time.Ticks % TimeSpan.TicksPerSecond)));
        Assert.Equal(3, read.ExitCode);
    }

    [Fact]
    public void A_health_status_is_one_action_of_two_words()
    {
        var read = WslcEventParsing.Parse(
            "2026-10-07T09:13:02.000000000Z container health_status: unhealthy 8449a1cce27f93 (image=alpine:latest, name=jade_wasatch)");

        Assert.NotNull(read);
        Assert.Equal("container", read.Type);
        Assert.Equal("health_status: unhealthy", read.Action);
        Assert.Equal("8449a1cce27f93", read.Id);
        Assert.Null(read.ExitCode);
        Assert.Equal("jade_wasatch", read.Name);
    }

    [Fact]
    public void A_volume_is_named_by_its_id_and_a_prune_names_nothing()
    {
        // wslc 3.0.4: a volume's events carry no name attribute, its id is its name.
        var created = WslcEventParsing.Parse("2026-10-19T20:30:05.000000000-05:00 volume create pgdata (driver=local)");

        Assert.NotNull(created);
        Assert.Equal("volume", created.Type);
        Assert.Equal("create", created.Action);
        Assert.Equal("pgdata", created.Id);
        Assert.Equal("pgdata", created.Name);

        // The aggregate prune's empty id is printed as a second space.
        var pruned = WslcEventParsing.Parse("2026-10-19T20:31:40.000000000-05:00 volume prune  (reclaimed=4096)");

        Assert.NotNull(pruned);
        Assert.Equal("prune", pruned.Action);
        Assert.Equal("", pruned.Id);
        Assert.Null(pruned.Name);
    }

    [Fact]
    public void What_is_not_an_event_is_not_read_as_one()
    {
        // The CLI's own abort arrives on the same stream when the session goes.
        Assert.Null(WslcEventParsing.Parse("Operation aborted "));
        Assert.Null(WslcEventParsing.Parse("Error code: E_ABORT"));
        Assert.Null(WslcEventParsing.Parse(""));
    }

    [Fact]
    public void A_notice_reaches_the_lists_it_is_about_and_no_others()
    {
        var containers = new ChangeNotice([ChangeNotice.Container]);

        Assert.True(containers.Touches(ChangeNotice.Container));
        Assert.False(containers.Touches(ChangeNotice.Image));
        Assert.True(ChangeNotice.Everything.Touches(ChangeNotice.Volume));

        // Up to wslc 3.0.3 there are no volume events, so that list keeps its clock.
        Assert.Contains(ChangeNotice.Container, ChangeNotice.AlwaysReported);
        Assert.Contains(ChangeNotice.Image, ChangeNotice.AlwaysReported);
        Assert.Contains(ChangeNotice.Network, ChangeNotice.AlwaysReported);
        Assert.DoesNotContain(ChangeNotice.Volume, ChangeNotice.AlwaysReported);
    }
}
