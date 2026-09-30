using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Host;
using WslcAgent.Server.Updates;

namespace WslcAgent.Server.Tests;

/// <summary>Settings → Update: the agent updating itself from its installer.</summary>
public sealed class AgentUpdateTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task A_build_folder_agent_keeps_the_switch_but_refuses_to_update_itself()
    {
        var client = factory.ClientWith(new FakeWslcRunner());

        var saved = await (await client.PutAsJsonAsync("/api/v1/agent/update/settings", new AgentUpdateSettings(false))).Content.ReadFromJsonAsync<AgentUpdateStatus>();
        var update = await client.PostAsync("/api/v1/agent/update", null);

        Assert.False(saved!.Settings.AutoUpdate);
        Assert.False(saved.Installed);
        Assert.Equal(AgentUpdateState.Idle, saved.State);
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
    }

    /// <summary>
    /// The line is written by PowerShell (AgentLogonTask.ps1 -Update, Set-Content
    /// -Encoding UTF8: a BOM and a line break) and read by the agent that starts
    /// next; if the two drift apart, a failed update goes unsaid.
    /// </summary>
    [Fact]
    public void The_updater_s_result_line_is_read_as_the_script_writes_it()
    {
        var folder = TestHost.TempDataDirectory();
        var ok = Path.Combine(folder, "ok.result");
        var failed = Path.Combine(folder, "failed.result");
        File.WriteAllText(ok, "ok|3010|0.1.76|C:\\Temp\\wslc-ai-agent-0.1.76.log\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(failed, "failed|1603|0.1.76|C:\\Temp\\wslc-ai-agent-0.1.76.log\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var done = AgentUpdateResult.Read(ok);
        var broken = AgentUpdateResult.Read(failed);

        Assert.Equal(new AgentUpdateResult(true, 3010, "0.1.76", "C:\\Temp\\wslc-ai-agent-0.1.76.log"), done);
        Assert.False(broken!.Ok);
        Assert.Equal(1603, broken.ExitCode);
        Assert.Null(AgentUpdateResult.Read(Path.Combine(folder, "none.result")));
    }

    /// <summary>
    /// Another agent on the same data folder — the development one beside the
    /// production one — writes the settings file; the running agent obeys it
    /// without a restart, and one taken away brings the defaults back (auto-update
    /// switched off there, and the agent that had read it in the morning
    /// updated itself).
    /// </summary>
    [Fact]
    public void Settings_written_by_another_agent_are_obeyed_without_a_restart()
    {
        var path = Path.Combine(TestHost.TempDataDirectory(), "agent-update.json");
        var settings = new UpdateSettingsFile(path);
        Assert.True(settings.Get().AutoUpdate);

        File.WriteAllText(path, "{ \"AutoUpdate\": false }");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        Assert.False(settings.Get().AutoUpdate);

        File.Delete(path);
        Assert.True(settings.Get().AutoUpdate);
    }

    /// <summary>The update's settings in a file of the test's own, shipping on as the agent's do.</summary>
    private sealed class UpdateSettingsFile(string path) : SavedSettings<AgentUpdateSettings>(path, new AgentUpdateSettings(true));
}
