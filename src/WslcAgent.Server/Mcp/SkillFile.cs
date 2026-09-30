using System.Reflection;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Mcp;

/// <summary>
/// The skill an AI client has to hold to operate this agent, and where to put
/// it. The file travels inside the agent (an embedded resource built from
/// <c>mcp-skill/skills/wslc/SKILL.md</c>), so what a machine hands out always
/// matches the tools that machine serves: a copy cannot fall behind them.
/// </summary>
public sealed class SkillFile
{
    /// <summary>The name every client expects; the folder it sits in is the client's.</summary>
    public const string FileName = "SKILL.md";

    /// <summary>The skill's own folder inside a client's skills directory.</summary>
    private const string SkillName = "wslc";

    /// <summary>
    /// Where each client keeps the skills it reads, under the account's folder.
    /// Not all of them call it the same: OpenClaw reads <c>plugin-skills</c>,
    /// and links its entries from elsewhere, so it also has its own installer —
    /// the download is the better path there.
    /// </summary>
    private static readonly (string Client, string Root, string Skills)[] Clients =
    [
        ("Hermes", ".hermes", "skills"),
        ("Claude Code", ".claude", "skills"),
        ("OpenClaw", ".openclaw", "plugin-skills"),
    ];

    private readonly string _text;
    private readonly string _home;

    /// <param name="home">The account's folder the clients live under; the agent's own unless a test says otherwise.</param>
    public SkillFile(string? home = null)
    {
        _home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("wslc-skill.md")
            ?? throw new InvalidOperationException("The agent was built without its skill (wslc-skill.md).");
        using var reader = new StreamReader(stream);
        _text = reader.ReadToEnd();
    }

    /// <summary>The skill itself, to download or to write.</summary>
    public string Text => _text;

    /// <summary>
    /// Every folder on this machine that a client would read this skill from:
    /// Hermes' own, one per Hermes profile (<c>.hermes/profiles/&lt;name&gt;</c>,
    /// which is where a machine really keeps them), and Claude Code's. The
    /// operator may write anywhere else — the folder is theirs to say — and
    /// each target says whether the skill is already there, so refreshing every
    /// profile after an update is a list to walk, not a memory exercise.
    /// </summary>
    public IReadOnlyList<SkillTarget> Targets()
    {
        var targets = new List<SkillTarget>();
        foreach (var (client, root, skills) in Clients)
        {
            var home = Path.Combine(_home, root);
            targets.Add(Target(client, home, skills));

            // Hermes reads each profile's own skills folder, so every profile is
            // a place to install: after an update they all need the new file.
            targets.AddRange(Profiles(Path.Combine(home, "profiles"))
                .Select(profile => Target($"{client} · {Path.GetFileName(profile)}", profile, skills)));
        }

        return targets;

        static SkillTarget Target(string client, string root, string skills)
        {
            var folder = Path.Combine(root, skills, SkillName);
            return new SkillTarget(client, folder, Directory.Exists(root), File.Exists(Path.Combine(folder, FileName)));
        }
    }

    /// <summary>The profiles a Hermes install holds, in name order; none when it has no profiles folder.</summary>
    private static IEnumerable<string> Profiles(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateDirectories(folder).Order() : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Each client's own installer line, pointing at this agent. Their
    /// installers are the standard path — they scan the skill, name it and
    /// record it the way that client expects — so the agent hands over the line
    /// instead of writing files into someone else's folders.
    /// </summary>
    /// <param name="skillUrl">Where this agent serves SKILL.md.</param>
    /// <param name="stagingFolder">A folder on this machine holding the skill, for an installer that reads a directory.</param>
    /// <param name="windows">The shell of the machine that will run it: this agent's own, or the far end of an SSH hop.</param>
    public static IReadOnlyList<SkillCommand> Commands(string skillUrl, bool windows)
    {
        // Every client is reached through its own installer; the skill is never
        // copied into someone else's folders behind their back. The two that
        // read a directory fetch the file into a temporary one first, with the
        // fetcher each shell has.
        var temp = windows ? $"%TEMP%\\{SkillName}" : $"/tmp/{SkillName}";
        var fetch = windows
            ? $"mkdir \"{temp}\" 2>nul & curl.exe -fsSL {skillUrl} -o \"{temp}\\{FileName}\""
            : $"mkdir -p {temp} && curl -fsSL {skillUrl} -o {temp}/{FileName}";
        var andThen = windows ? " & " : " && ";
        var claude = windows ? $"%USERPROFILE%\\.claude\\skills\\{SkillName}" : $"~/.claude/skills/{SkillName}";
        var claudeFetch = windows
            ? $"mkdir \"{claude}\" 2>nul & curl.exe -fsSL {skillUrl} -o \"{claude}\\{FileName}\""
            : $"mkdir -p {claude} && curl -fsSL {skillUrl} -o {claude}/{FileName}";

        return
        [
            // Hermes resolves a URL only through the well-known skills index of
            // a host; a bare link to a file comes back as "could not fetch from
            // any source", which is what it did before the agent served one.
            new("Hermes", $"hermes skills install {WellKnown(skillUrl)} --yes",
                "Hermes reads the agent's own skills endpoint (/.well-known/skills), so the line needs nothing else. Each profile is its own command — `alex skills install …` installs into the profile alex — so a machine with profiles takes one run each."),
            new("OpenClaw", $"{fetch}{andThen}openclaw skills install \"{temp}\" --as {SkillName} --force",
                "OpenClaw installs from a directory, so the line fetches the file into a temporary one and hands that to its installer."),
            new("Claude Code", claudeFetch,
                $"Claude Code has no installer: it reads {FileName} from its own skills folder, so the line fetches it straight there."),
        ];
    }

    /// <summary>
    /// The same skill as a well-known endpoint of the same agent: the address of
    /// <c>/api/v1/mcp/skill/SKILL.md</c> with its path swapped for the one a
    /// client's installer knows how to resolve.
    /// </summary>
    private static string WellKnown(string skillUrl) =>
        skillUrl[..skillUrl.IndexOf("/api/v1/", StringComparison.Ordinal)] + $"/.well-known/skills/{SkillName}";

    /// <summary>
    /// A folder on this machine holding the skill, for the installers that take
    /// a directory. Written on the way out, so it is never an older copy.
    /// </summary>
    public string Stage(string dataDirectory)
    {
        var folder = Path.Combine(dataDirectory, "skill", SkillName);
        Install(folder);
        return folder;
    }

    /// <summary>
    /// Writes the skill into <paramref name="folder"/>, creating it. The folder
    /// is the caller's choice on purpose: one Hermes profile is one folder, and
    /// the same skill goes into as many as the operator has.
    /// </summary>
    public SkillInstalled Install(string folder)
    {
        var target = folder.Trim();
        if (target.Length == 0 || !Path.IsPathFullyQualified(target))
        {
            throw new ArgumentException("A full path to the client's skill folder is needed, e.g. C:\\Users\\me\\.hermes\\skills\\wslc.", nameof(folder));
        }

        var existed = Directory.Exists(target);
        Directory.CreateDirectory(target);
        var file = Path.Combine(target, FileName);
        File.WriteAllText(file, _text);
        return new SkillInstalled(file, !existed);
    }
}
