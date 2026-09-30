using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Overview;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

/// <summary>The System page's reading of the CLI, and its endpoints standing when parts of it fail.</summary>
public sealed class SystemEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void Info_flattens_client_and_server_fields()
    {
        using var json = JsonDocument.Parse("""
            {"Client":{"Version":"2.9.12","KernelVersion":"6.6.87","WindowsVersion":"10.0.26340","SettingsFile":"C:\\s.json","Direct3DVersion":"1.6","DxCoreVersion":"1.7"},
             "Server":{"SessionManagerVersion":"2.9.12","Sessions":[]}}
            """);

        var info = SystemParsing.RuntimeInfo(json.RootElement);

        Assert.Equal(new SystemRuntimeInfo("2.9.12", "6.6.87", "10.0.26340", "2.9.12", "1.6", "1.7", "C:\\s.json"), info);
        Assert.True(SystemParsing.RuntimeInfo(default).IsEmpty);
    }

    [Fact]
    public void Session_list_splits_on_wide_gaps_and_keeps_names_with_spaces()
    {
        var sessions = SystemParsing.ActiveSessions("ID    CREATOR PID    NAME\n7     1234           wslc-cli-user\n9     n/a            my session\n");

        Assert.Equal([new ActiveSession("7", 1234, "wslc-cli-user"), new ActiveSession("9", null, "my session")], sessions);
    }

    [Fact]
    public void Created_reads_the_cli_offset_form_unix_and_iso()
    {
        Assert.Equal(new DateTimeOffset(2026, 8, 27, 9, 7, 36, TimeSpan.FromHours(-5)), SystemParsing.Created("2026-08-27 09:07:36 -0500 GMT-5"));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), SystemParsing.Created("1700000000"));
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), SystemParsing.Created("2026-01-02T03:04:05Z"));
        Assert.Null(SystemParsing.Created("yesterday"));
    }

    [Fact]
    public void Cleanup_summary_prefers_what_wslc_reported()
    {
        Assert.Equal(1_500_000_000, SystemParsing.ReclaimedBytes("Deleted: x\nTotal reclaimed space: 1.5GB"));
        Assert.Null(SystemParsing.ReclaimedBytes("nothing"));
        Assert.Equal(
            "Image prune completed. WSLC reported 1.4 GiB reclaimed inside the store. VHDX file length on disk did not shrink yet.",
            SystemParsing.CleanupSummary("images", 1_500_000_000, 0, 10));
        Assert.Equal(
            "Network prune completed. VHDX file length on disk shrank by 1 KiB.",
            SystemParsing.CleanupSummary("networks", null, 1024, null));
    }

    [Fact]
    public void Compaction_targets_the_selected_session_and_no_other()
    {
        var store = new SessionStoreUsage("C:\\s", [Store("a"), Store("b")], 0, 0, 0, "", 0, "");
        ActiveSession[] active = [new("1", null, "a"), new("2", null, "b")];

        Assert.Equal(("b", "C:\\s\\b\\storage.vhdx"), SystemParsing.CompactionTarget(store, active, "b"));
        Assert.Equal(("a", "C:\\s\\a\\storage.vhdx"), SystemParsing.CompactionTarget(store, active, "a"));

        // Nothing running, and another session running, come to the same thing:
        // the selected store's file, and no name to block it (a compaction
        // was refused over someone else's store).
        Assert.Equal(("", "C:\\s\\b\\storage.vhdx"), SystemParsing.CompactionTarget(store, [], "b"));
        Assert.Equal(("", "C:\\s\\b\\storage.vhdx"), SystemParsing.CompactionTarget(store, [new("1", null, "a")], "b"));
    }

    [Fact]
    public async Task Overview_keeps_what_worked_when_parts_fail()
    {
        var runner = new FakeWslcRunner()
            .Answer("version", "wslc 2.9.12\n")
            .Fail("info --format json", "unrecognized command")
            .Answer("system info --format json", """{"Client":{"Version":"2.9.12"},"Server":{}}""")
            .Fail("image list --format json", "boom")
            // The session the agent targets: it is the one that blocks compaction.
            .Answer("system session list", $"ID  CREATOR PID  NAME\n3   77           {SessionStores.Default}\n");

        var body = await factory.ClientWith(runner).GetFromJsonAsync<SystemOverview>("/api/v1/system");

        Assert.NotNull(body);
        Assert.Equal("wslc 2.9.12", body.Version);
        Assert.Equal("2.9.12", body.Info.ClientVersion);
        Assert.Contains("boom", body.ImageError);
        Assert.True(body.CompactionBlocked);
        Assert.Equal(SessionStores.Default, body.PrimaryActiveSession);
    }

    [Fact]
    public async Task Another_session_running_does_not_block_compaction()
    {
        var runner = new FakeWslcRunner()
            .Answer("version", "wslc 2.9.12\n")
            .Answer("system info --format json", """{"Client":{"Version":"2.9.12"},"Server":{}}""")
            .Answer("system session list", "ID  CREATOR PID  NAME\n3   77           someone-elses-session\n");

        var body = await factory.ClientWith(runner).GetFromJsonAsync<SystemOverview>("/api/v1/system");

        Assert.NotNull(body);
        Assert.False(body.CompactionBlocked);
        Assert.Equal("", body.PrimaryActiveSession);
    }

    [Fact]
    public async Task Cleanup_prunes_every_unused_image_and_rejects_other_targets()
    {
        var runner = new FakeWslcRunner()
            .Answer("image list --format json", "")
            .Answer("image prune -f -a", "Total reclaimed space: 2MB");
        var client = factory.ClientWith(runner);

        var result = await (await client.PostAsync("/api/v1/system/cleanup/images", null)).Content.ReadFromJsonAsync<CleanupResult>();
        var rejected = await client.PostAsync("/api/v1/system/cleanup/containers", null);

        Assert.NotNull(result);
        Assert.Equal(2_000_000, result.ReclaimedBytes);
        Assert.Equal("wslc image prune -f -a", result.Command);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    private static SessionStore Store(string name) =>
        new(name, $"C:\\s\\{name}", $"C:\\s\\{name}\\storage.vhdx", $"C:\\s\\{name}\\swap.vhdx", 0, 0, 0, null, false, false);
}
