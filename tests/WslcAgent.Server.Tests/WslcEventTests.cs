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

        // wslc reports no volume events at all, so that list keeps its clock.
        Assert.True(ChangeNotice.IsReported(ChangeNotice.Container));
        Assert.True(ChangeNotice.IsReported(ChangeNotice.Image));
        Assert.True(ChangeNotice.IsReported(ChangeNotice.Network));
        Assert.False(ChangeNotice.IsReported(ChangeNotice.Volume));
    }
}
