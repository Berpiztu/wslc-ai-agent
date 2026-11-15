using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Overview;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

/// <summary>The host terminal's status and its jobs; the shell itself needs a real console.</summary>
public sealed class TerminalEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Status_names_the_local_host_shell()
    {
        var status = await factory.ClientWith(new FakeWslcRunner()).GetFromJsonAsync<TerminalStatus>("/api/v1/terminal/status");

        Assert.NotNull(status);
        Assert.Equal("local", status.Mode);
        Assert.Equal("local host shell", status.Label);
    }

    [Fact]
    public async Task Compaction_is_refused_while_a_session_is_active_and_unknown_jobs_are_not_found()
    {
        // The session whose VHDX would be compacted, and no other: another
        // store being in use does not hold this file open. Its name is this machine's, not a literal, or
        // the test would only refuse on the machine it was written on.
        var runner = new FakeWslcRunner().Answer("system session list", $"ID  CREATOR PID  NAME\n3   77           {SessionStores.Default}\n");
        var client = factory.ClientWith(runner);

        var blocked = await client.PostAsync("/api/v1/terminal/jobs/compact-vhdx", null);
        var unknown = await client.PostAsync("/api/v1/terminal/jobs/format-disk", null);

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains(SessionStores.Default, await blocked.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public void Script_quotes_paths_and_never_terminates_a_session()
    {
        var script = CompactVhdxScript.Build(@"C:\Users\o'neil\wslc\sessions\s\storage.vhdx", "s", @"C:\wslc\wslc.exe");

        Assert.Contains(@"$path = 'C:\Users\o''neil\wslc\sessions\s\storage.vhdx'", script);
        Assert.Contains(@"$storageDir = 'C:\Users\o''neil\wslc\sessions\s'", script);
        Assert.Contains("Optimize-VHD -Path $path -Mode Full", script);
        Assert.DoesNotContain("session terminate", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Script_removes_from_the_store_only_on_a_yes()
    {
        var script = CompactVhdxScript.Build(@"C:\s\storage.vhdx", "s", @"C:\wslc\wslc.exe");

        // Trimming is what it does unless the answer to its question is yes.
        Assert.Contains("$mode = 'trim'", script);
        Assert.Contains("if ($answer -match '^\\s*y(es)?\\s*$') { $mode = 'clean' }", script);
        // And q leaves before anything is removed, trimmed or compacted.
        Assert.Contains("if ($answer -match '^\\s*q(uit)?\\s*$') {", script);
        Assert.True(
            script.IndexOf("Read-Host", StringComparison.Ordinal) < script.IndexOf("Enter-Store $mode", StringComparison.Ordinal),
            "The store is entered to remove or trim only after the question.");
    }

    [Fact]
    public void Store_script_is_typed_in_lines_a_terminal_takes_and_removes_only_when_cleaning()
    {
        var typed = StoreCleanupScript.Typed(StoreCleanupScript.Report);

        Assert.All(typed.Split('\n'), line => Assert.True(line.Length < 100, line));
        Assert.EndsWith($"sh {StoreCleanupScript.GuestPath} report; exit\n", typed);

        // Every removal sits behind the clean mode, and none names a volume or a container's folder.
        var text = StoreCleanupScript.Text.ReplaceLineEndings("\n");
        var cleaning = text.IndexOf("if [ \"$mode\" = clean ]; then", StringComparison.Ordinal);
        var firstRemoval = text.IndexOf("drop \"$(cat", StringComparison.Ordinal);
        Assert.True(cleaning >= 0 && firstRemoval > cleaning);
        Assert.DoesNotContain("rm -rf $root/volumes", text);
        Assert.DoesNotContain("rm -rf $root/containers", text);
        Assert.DoesNotContain("docker ", text);
    }
}
