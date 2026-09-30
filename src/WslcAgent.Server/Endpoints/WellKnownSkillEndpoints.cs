using System.Text;
using WslcAgent.Server.Mcp;

namespace WslcAgent.Server.Endpoints;

/// <summary>
/// The agent as a skills endpoint, in the shape AI clients look for:
/// <c>/.well-known/skills/index.json</c> listing what it offers, and each
/// skill's <c>SKILL.md</c> beside it. Hermes reads a URL only in this shape —
/// its installer resolves <c>&lt;host&gt;/.well-known/skills/&lt;name&gt;</c>
/// through the index and refuses a bare link to a file — so serving it is what
/// makes "install the skill from the agent" one command anywhere.
/// </summary>
public static class WellKnownSkillEndpoints
{
    private const string Skill = "wslc";
    private const string Base = "/.well-known/skills";

    public static WebApplication MapWellKnownSkillEndpoints(this WebApplication app)
    {
        // Not under /api, and public on purpose: a client's installer fetches it
        // with no headers of ours. It is the same document Settings hands out,
        // and it carries nothing an agent's own sign-in page does not.
        app.MapGet($"{Base}/index.json", (SkillFile skill) => Results.Json(new
        {
            skills = new[]
            {
                new
                {
                    name = Skill,
                    description = Description(skill.Text),
                    files = new[] { SkillFile.FileName },
                },
            },
        })).WithName("WellKnownSkillIndex");

        app.MapGet($"{Base}/{Skill}/{SkillFile.FileName}", (SkillFile skill) =>
                Results.Text(skill.Text, "text/markdown", Encoding.UTF8))
            .WithName("WellKnownSkillFile");

        return app;
    }

    /// <summary>The skill's own description, so the index cannot drift from the file it points at.</summary>
    private static string Description(string text) =>
        text.Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
            ?["description:".Length..].Trim() ?? "Operate a WSLC host through this agent's MCP tools.";
}
