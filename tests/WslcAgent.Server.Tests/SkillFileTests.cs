using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Mcp;

namespace WslcAgent.Server.Tests;

/// <summary>
/// The skill the agent hands out: the file itself, where it says a client keeps
/// it, and writing it there. The point of it travelling inside the agent is
/// that no copy can describe tools this agent does not have.
/// </summary>
public sealed class SkillFileTests
{
    [Fact]
    public void The_agent_carries_the_skill_it_serves()
    {
        var text = new SkillFile().Text;

        Assert.StartsWith("---", text);
        Assert.Contains("name: wslc", text);
        Assert.Contains("list_containers", text);
    }

    /// <summary>Hermes reads a profile's own skills folder, so every profile is a place to install.</summary>
    [Fact]
    public void Every_hermes_profile_is_a_place_to_install()
    {
        var home = TestHost.TempDataDirectory();
        Directory.CreateDirectory(Path.Combine(home, ".hermes", "profiles", "leire"));
        Directory.CreateDirectory(Path.Combine(home, ".hermes", "profiles", "alex"));

        var targets = new SkillFile(home).Targets();

        Assert.Equal(
            ["Hermes", "Hermes · alex", "Hermes · leire", "Claude Code", "OpenClaw"],
            targets.Select(target => target.Client));
        Assert.Equal(Path.Combine(home, ".hermes", "profiles", "alex", "skills", "wslc"), targets[1].Folder);
        // OpenClaw calls that folder plugin-skills, which is what it reads.
        Assert.Equal(Path.Combine(home, ".openclaw", "plugin-skills", "wslc"), targets[^1].Folder);
        Assert.True(targets[0].ClientFound);
        Assert.False(targets[^1].ClientFound);
        Assert.All(targets, target => Assert.False(target.Installed));
    }

    [Fact]
    public void Installing_writes_the_file_and_says_where()
    {
        var home = TestHost.TempDataDirectory();
        var skill = new SkillFile(home);
        var folder = Path.Combine(home, ".hermes", "profiles", "leire", "skills", "wslc");

        var installed = skill.Install(folder);

        Assert.Equal(Path.Combine(folder, "SKILL.md"), installed.Path);
        Assert.True(installed.FolderCreated);
        Assert.Equal(skill.Text, File.ReadAllText(installed.Path));
        Assert.True(skill.Targets().Single(target => target.Client == "Hermes · leire").Installed);

        // Again over an older copy: that is how a profile is refreshed.
        Assert.False(skill.Install(folder).FolderCreated);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("skills/wslc")]
    public void A_folder_that_is_not_a_full_path_is_refused(string folder) =>
        Assert.Throws<ArgumentException>(() => new SkillFile().Install(folder));
}

/// <summary>The same two things over HTTP, which is how Settings reaches them.</summary>
public sealed class SkillEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task The_skill_downloads_as_a_markdown_file()
    {
        var response = await factory.ClientWith(new FakeWslcRunner()).GetAsync("/api/v1/mcp/skill");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("SKILL.md", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Contains("name: wslc", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The install runs that client's own command and brings back what it said.
    /// Here there is no client to answer, which is the point: the endpoint
    /// reports the line and the failure instead of pretending it worked.
    /// </summary>
    [Fact]
    public async Task Installing_runs_the_clients_own_command_and_reports_it()
    {
        var client = factory.ClientWith(new FakeWslcRunner());

        var response = await client.PostAsJsonAsync("/api/v1/mcp/skill/install",
            new SkillInstallRequest("Hermes", Address: "https://agent.example.com", Profile: "alex"));
        var result = await response.Content.ReadFromJsonAsync<SkillInstallResult>();

        Assert.NotNull(result);
        Assert.Equal("Hermes", result.Client);
        // The profile is the command, the address is the one the caller gave, and
        // the URL is the well-known one — the only shape Hermes resolves.
        Assert.Equal("alex skills install https://agent.example.com/.well-known/skills/wslc --yes", result.Command);
        Assert.False(string.IsNullOrWhiteSpace(result.Output));
    }

    /// <summary>A profile name is a command here, so it may be a name and nothing else.</summary>
    [Fact]
    public async Task A_profile_that_is_not_a_name_is_refused()
    {
        var client = factory.ClientWith(new FakeWslcRunner());

        var response = await client.PostAsJsonAsync("/api/v1/mcp/skill/install",
            new SkillInstallRequest("Hermes", Profile: "alex; rm -rf /"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The shape an AI client's installer looks for. Hermes resolves a skill
    /// through a host's /.well-known/skills index — a bare link to a file comes
    /// back as "could not fetch from any source" — so this is what makes the
    /// agent installable from anywhere, and it is public because the installer
    /// fetches it with no headers of ours.
    /// </summary>
    [Fact]
    public async Task The_agent_is_a_well_known_skills_endpoint()
    {
        var client = factory.ClientWith(new FakeWslcRunner());
        client.DefaultRequestHeaders.Add(TestHost.RemoteHeader, "1");

        var index = await client.GetFromJsonAsync<JsonElement>("/.well-known/skills/index.json");
        var skill = await client.GetAsync("/.well-known/skills/wslc/SKILL.md");

        var entry = index.GetProperty("skills")[0];
        Assert.Equal("wslc", entry.GetProperty("name").GetString());
        Assert.Contains("WSLC", entry.GetProperty("description").GetString());
        Assert.Equal(["SKILL.md"], entry.GetProperty("files").EnumerateArray().Select(file => file.GetString()));
        Assert.Equal(HttpStatusCode.OK, skill.StatusCode);
        Assert.Contains("name: wslc", await skill.Content.ReadAsStringAsync());
    }

    /// <summary>The page offers what a machine has, and this one answers for itself.</summary>
    [Fact]
    public async Task The_agents_own_machine_answers_which_clients_it_has()
    {
        var client = factory.ClientWith(new FakeWslcRunner());

        var clients = await client.GetFromJsonAsync<IReadOnlyList<SkillClient>>("/api/v1/mcp/skill/clients");

        Assert.NotNull(clients);
        Assert.Equal(["Hermes", "OpenClaw", "Claude Code"], clients.Select(found => found.Client));
    }
}
