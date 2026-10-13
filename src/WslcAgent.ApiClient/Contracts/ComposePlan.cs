namespace WslcAgent.ApiClient.Contracts;

/// <summary>
/// Body of <c>POST /api/v1/projects/plan</c>: a Compose file to read, by its
/// path on the agent's machine or as its text. Nothing is run.
/// </summary>
/// <param name="Path">The Compose file, or the folder it is in, on the agent's machine. It gives relative paths, <c>.env</c> and <c>env_file</c> something to stand on.</param>
/// <param name="Yaml">The file's text, when there is no path: a pasted file has no folder, so what needs one is named as not available.</param>
/// <param name="Name">The project's name; empty takes the file's own <c>name</c>, then its folder's.</param>
/// <param name="Variables">Variables for <c>${NAME}</c>, <c>KEY=value</c> each; they win over the folder's <c>.env</c>.</param>
/// <param name="Profiles">The profiles asked for: a service with profiles is left out unless one of them is here.</param>
/// <param name="Services">The services asked for by name: they are taken, with the ones they depend on, and every other is left out, whatever the profiles say. A name the file does not have is refused. None, the whole file is read and the profiles decide.</param>
/// <param name="Folder">The context: the folder of the agent's machine the file's relative paths, its <c>build</c>, its <c>.env</c> and its <c>env_file</c> start from. Empty takes the folder the file is in; a file given as text takes the one its project was last saved with on the agent, and has none when the project is new and none is said here. It need not be there yet: the folders the file mounts under it are made when its containers start.</param>
/// <param name="Contexts">
/// The build contexts sent to the agent (<c>POST /api/v1/projects/contexts</c>)
/// by a client on another machine, where the file's folders are: each
/// context as the file writes it (<c>./app</c>, <c>.</c>) with the id the
/// agent gave what was sent for it. A <c>build</c> named here is built from
/// that, read by <c>wslc build</c> from its standard input. The id may be of a context not
/// sent yet (<see cref="ProjectContextRequest.IdOf"/>): the file is then
/// read as it will be once it is, which is how a client asks what a save
/// would do before it sends anything, and the build itself says so if it
/// comes before the context. An empty id says only that the client will send
/// one: the <c>build</c> is taken as one that can be made.
/// </param>
/// <param name="ClientFolder">
/// The folder the file is in on the machine of whoever sends it, when that
/// is another machine: where a project new to the agent is to live, at the
/// same path on the agent's own, when no <paramref name="Folder"/> is named
/// and the agent keeps none for the project. One folder, the same on both
/// machines, is what a Compose file's relative paths mean to whoever wrote it.
/// </param>
public sealed record ComposePlanRequest(
    string Path = "",
    string Yaml = "",
    string Name = "",
    IReadOnlyList<string>? Variables = null,
    IReadOnlyList<string>? Profiles = null,
    string Folder = "",
    IReadOnlyList<string>? Services = null,
    IReadOnlyDictionary<string, string>? Contexts = null,
    string ClientFolder = "");

/// <summary>
/// Body of <c>POST /api/v1/projects/contexts</c>: a build context as the
/// list of what is in it. Its files are not in the request: each is named by
/// the hash of its bytes, and has been sent before (<c>POST
/// /api/v1/projects/contexts/files</c>) unless the agent had it already.
/// </summary>
/// <param name="Entries">The folders and the files of the context, a folder before what is in it.</param>
/// <param name="Skipped">The entries of the folder the client could not read and left out, by their paths: written in the agent's log, where whoever asks later why a build lacks a file finds it.</param>
/// <param name="Project">The project the context is for, and <paramref name="Context"/> the context as its Compose file writes it: what that line of the log says it is about.</param>
public sealed record ProjectContextRequest(IReadOnlyList<ProjectContextEntry> Entries, IReadOnlyList<string>? Skipped = null, string Project = "", string Context = "")
{
    /// <summary>
    /// What a context is known by: its entries' names and its files' bytes,
    /// in the order they come. Not when each was last written: a folder
    /// checked out again holds the same files, and is the same context.
    /// Worked out the same way by the agent, which names what it keeps by
    /// it, and by a client, which can then say which context a file is
    /// built from before it has sent any of it.
    /// </summary>
    public static string IdOf(IReadOnlyList<ProjectContextEntry> entries) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join('\n', entries.Select(entry => $"{entry.Name}\0{entry.Blob}")))))[..32];
}

/// <summary>
/// Body of <c>POST /api/v1/logs/client</c>: something a client showed its
/// user as a warning or an error, for the agent to write in its log.
/// </summary>
/// <param name="Level"><see cref="Warning"/> or <see cref="Error"/>.</param>
/// <param name="Text">What was shown.</param>
/// <param name="Where">Which client and where in it: <c>web, /containers</c>, <c>wslc-compose up</c>.</param>
public sealed record ClientReport(string Level, string Text, string Where = "")
{
    public const string Warning = "warning";

    public const string Error = "error";
}

/// <summary>A folder or a file of a build context.</summary>
/// <param name="Name">Its path in the context, with forward slashes: <c>src/app.py</c>.</param>
/// <param name="Size">A file's size in bytes; 0 for a folder.</param>
/// <param name="Modified">When it was last written, in seconds since 1970 (UTC): what the build sees as its date.</param>
/// <param name="Blob">The SHA-256 of a file's bytes, in lower-case hex; empty for a folder.</param>
public sealed record ProjectContextEntry(string Name, long Size, long Modified, string Blob);

/// <summary>
/// Body and answer of <c>POST /api/v1/projects/contexts/files/missing</c>:
/// the files a client has, by the SHA-256 of their bytes, and of those the
/// ones the agent lacks, which are the ones to send.
/// </summary>
public sealed record ContextFiles(IReadOnlyList<string> Hashes);

/// <summary>A build context kept by the agent: answer of <c>POST /api/v1/projects/contexts</c>.</summary>
/// <param name="Id">What the agent knows it by, and the request of a Compose file names it by: made from what it holds, so the same folder sent again is the same context.</param>
/// <param name="Size">The size of its files, in bytes.</param>
/// <param name="Files">How many files it has.</param>
public sealed record ProjectContext(string Id, long Size, int Files = 0);

/// <summary>Body of <c>POST /api/v1/projects/open</c>: where Windows' Open dialog starts.</summary>
/// <param name="Folder">A folder of the agent's machine, usually that of the file opened last; empty leaves it to Windows.</param>
public sealed record OpenComposeFileRequest(string Folder = "");

/// <summary>
/// Answer of <c>POST /api/v1/projects/open</c>: the Compose file chosen in
/// Windows' Open dialog on the agent's desktop.
/// </summary>
/// <param name="Path">Its full path on the agent's machine, which a browser's own dialog never gives; empty when the dialog was cancelled.</param>
/// <param name="Yaml">Its text.</param>
public sealed record ComposeFileOpened(string Path, string Yaml);

/// <summary>
/// A Compose file as the agent would bring it up: each service as the launch
/// request the Run form sends, the networks and volumes it needs, and what
/// the file asks for that cannot be given. The reading alone; nothing has run.
/// </summary>
/// <param name="Name">The project: what its containers, networks and volumes are named after.</param>
/// <param name="File">The file read, on the agent's machine; empty for a pasted one.</param>
/// <param name="Services">Every service of the file, in the order they would start.</param>
/// <param name="Networks">The networks the services stand on.</param>
/// <param name="Volumes">The named volumes the services mount.</param>
/// <param name="Unsupported">What the file asks for that has no equivalent here, each as <c>service: what</c>. The user is told before anything runs.</param>
/// <param name="NotNeeded">What the file asks for that WSLC gives already.</param>
/// <param name="Warnings">What was read with a doubt: a variable that is not set, a file left unread.</param>
/// <param name="Lines">The file itself, line by line, each with what the agent makes of it: the plan as the user reads it, beside what they wrote.</param>
/// <param name="Notes">The warnings that are about the file as a whole and not about one of its lines: what a screen says above the lines, which carry the rest.</param>
/// <param name="Variables">The variables the file asks for (<c>${NAME}</c>), in the order it first names them, each with the value it was read with: the file's parameters, for whoever runs it to set.</param>
/// <param name="Contexts">The build contexts the file names by a relative path: what a client on another machine, where those folders are, sends to the agent (<see cref="ComposePlanRequest.Contexts"/>). Null from an agent too old to take one.</param>
/// <param name="Folder">The folder of the agent's machine the file's relative paths were read from: the one asked for, the file's own, or the one its project was last saved with; empty when there was none.</param>
/// <param name="NeedsFolder">The file names something by a relative path (a mount, an <c>env_file</c>, a build nobody sends) and there was no folder of the agent's machine to read it from: those lines are not given until one is named.</param>
public sealed record ComposePlan(
    string Name,
    string File,
    IReadOnlyList<ComposeService> Services,
    IReadOnlyList<ComposeNetwork> Networks,
    IReadOnlyList<ComposeVolume> Volumes,
    IReadOnlyList<string> Unsupported,
    IReadOnlyList<string> NotNeeded,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ComposeLine> Lines,
    IReadOnlyList<string> Notes,
    IReadOnlyList<ComposeVariable> Variables,
    IReadOnlyList<ComposeContext>? Contexts = null,
    string Folder = "",
    bool NeedsFolder = false);

/// <summary>A build context a Compose file names by a relative path.</summary>
/// <param name="Path">The context as the file writes it (<c>./app</c>, <c>.</c>): what the request names it by.</param>
/// <param name="Needed">A service that would be taken is built from it; false, only services left out are, and it need not be sent.</param>
public sealed record ComposeContext(string Path, bool Needed);

/// <summary>A variable a Compose file asks for.</summary>
/// <param name="Name">Its name, as in <c>${NAME}</c>.</param>
/// <param name="Value">What it was read as where the file first names it: its value, or the default the file gives there when it has none.</param>
/// <param name="Set">Something gives it a value: the request, the environment or the <c>.env</c> beside the file. False, it is the file's default, or empty.</param>
public sealed record ComposeVariable(string Name, string Value, bool Set);

/// <summary>
/// Body of <c>POST /api/v1/projects/preview</c> and <c>/projects/up</c>: a
/// Compose file to save as a project.
/// </summary>
/// <param name="File">The file, as for a plan.</param>
/// <param name="Start">Start the containers, in the order they depend on each other (Run); false creates them and leaves them stopped (Save).</param>
/// <param name="RemoveOrphans">Remove the project's containers whose service the file no longer has. Never assumed: the user is asked first.</param>
/// <param name="AddServices">
/// The services this save takes are added to the ones the project is kept
/// with, and do not take their place: how a command line brings up one more
/// service of a project (<c>up web</c>, and later <c>up</c>), where a window
/// that shows the whole choice sets it as it is shown. Either way the
/// project is kept with its services by name, the ones the save took even
/// when none was named: it is what its window shows ticked.
/// </param>
public sealed record ProjectUpRequest(ComposePlanRequest File, bool Start = false, bool RemoveOrphans = false, bool AddServices = false);

/// <summary>
/// Body of <c>POST /api/v1/projects/build</c>: a Compose file whose images
/// to build, and nothing else of it made.
/// </summary>
/// <param name="File">The file, as for a plan.</param>
/// <param name="NoCache">Build every layer again, taking nothing an earlier build left.</param>
/// <param name="Pull">Ask the registry for the base images again, instead of taking the ones on the machine.</param>
public sealed record ProjectBuildRequest(ComposePlanRequest File, bool NoCache = false, bool Pull = false);

/// <summary>
/// What saving a Compose file would do, with nothing done: what the question
/// before the save is made of. Containers are named as they are on the
/// machine.
/// </summary>
/// <param name="Exists">The project is already on the machine: the save applies the file to it.</param>
/// <param name="Create">Services with no container yet.</param>
/// <param name="Recreate">Services whose container was made from something else than the file says now: the file changed, or (<paramref name="Edited"/>) the container did.</param>
/// <param name="Edited">Those of <paramref name="Recreate"/> whose container was changed by hand since the file made it (View &amp; edit): the save puts it back as the file says, and what was changed is lost, the names it is published on apart. Null from an agent too old to say.</param>
/// <param name="Keep">Services whose container is as the file says.</param>
/// <param name="Orphans">The project's containers whose service the file no longer has: removed only if the user says so.</param>
/// <param name="Networks">Networks to create.</param>
/// <param name="Volumes">Volumes to create.</param>
/// <param name="Builds">Images to build.</param>
/// <param name="Missing">What the file names as external and the machine does not have, each as <c>network NAME</c> or <c>volume NAME</c>: the project does not create them, so the save would stop there. Null from an agent too old to say.</param>
public sealed record ProjectPreview(
    string Name,
    bool Exists,
    IReadOnlyList<string> Create,
    IReadOnlyList<string> Recreate,
    IReadOnlyList<string> Keep,
    IReadOnlyList<string> Orphans,
    IReadOnlyList<string> Networks,
    IReadOnlyList<string> Volumes,
    IReadOnlyList<string> Builds,
    IReadOnlyList<string> Unsupported,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string>? Edited = null,
    IReadOnlyList<string>? Missing = null);

/// <summary>A project's save, or a build of its images, under way or over: <c>POST /api/v1/projects/up</c> and <c>/projects/build</c> answer it at once, <c>GET /api/v1/projects/jobs/{id}</c> as it goes.</summary>
/// <param name="Step">What it is doing now, in words for the window that waits.</param>
/// <param name="Log">Every step so far, and what the builds printed.</param>
/// <param name="Error">Why it stopped; empty while it runs and once it is done.</param>
/// <param name="Pct">How far it has got: what it has made of what it has to make.</param>
public sealed record ProjectJob(string Id, string Name, string State, string Step, IReadOnlyList<string> Log, string Error, int Pct = 0)
{
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "error";
    public const string Cancelled = "cancelled";
}

/// <summary>Answer of <c>GET /api/v1/projects/{name}</c>: the Compose file kept with a project, as it was last brought up.</summary>
/// <param name="File">The file it was read from, on the agent's machine; empty when it was pasted.</param>
/// <param name="Folder">The context it was read with.</param>
/// <param name="Yaml">Its text as it was then.</param>
/// <param name="Variables">The variables it was read with, <c>KEY=value</c> each.</param>
/// <param name="Profiles">The profiles it was read with.</param>
/// <param name="Services">The services it was saved for by name, when it was not for the whole file: what the project's start goes by. Null or none, the whole file.</param>
public sealed record StoredProject(string Name, string File, string Folder, string Yaml, IReadOnlyList<string> Variables, IReadOnlyList<string> Profiles, DateTimeOffset SavedAt, IReadOnlyList<string>? Services = null);

/// <summary>A container of a project; rows of <c>GET /api/v1/projects/{name}/containers</c>, in the order the project's services start in.</summary>
/// <param name="Id">The container's id, as the containers list gives it.</param>
/// <param name="Service">Its service in the project's Compose file.</param>
/// <param name="DependsOn">The services its own depends on, by the file kept with the project: it is not started when one of them did not start.</param>
/// <param name="Taken">Its service is one the project was saved for: of the profiles and the services it was saved with. A start of the project starts these and leaves the others as they are.</param>
public sealed record ProjectMember(string Id, string Name, string Service, bool Running, IReadOnlyList<string> DependsOn, bool Taken = true);

/// <summary>
/// Answer of <c>GET /api/v1/projects/{name}/logs</c>: what the project's
/// containers wrote, as one log, in the order it was written.
/// </summary>
public sealed record ProjectLogs(IReadOnlyList<ProjectLogLine> Lines);

/// <summary>A line of a project's logs.</summary>
/// <param name="Container">The container that wrote it, by name: what the line is said by.</param>
/// <param name="Service">Its service in the project's Compose file.</param>
/// <param name="Time">When, as <c>wslc</c> stamps it (RFC 3339 to the nanosecond, UTC): what the lines are ordered by, and what a reader asks the next lines after. Empty for a line that carries none.</param>
/// <param name="Text">The line as written.</param>
public sealed record ProjectLogLine(string Container, string Service, string Time, string Text);

/// <summary>Body of <c>POST /api/v1/projects/{name}/down</c>.</summary>
/// <param name="Volumes">Remove the project's volumes too, and what they hold. Never assumed: the user is asked first.</param>
public sealed record ProjectDownRequest(bool Volumes = false);

/// <summary>Answer of a verb on a whole project (<c>start</c>, <c>stop</c>, <c>restart</c>, <c>down</c>).</summary>
/// <param name="Done">What the verb went through on: containers by name, and for a down <c>network NAME</c> and <c>volume NAME</c>.</param>
public sealed record ProjectResult(string Name, IReadOnlyList<string> Done);

/// <summary>One line of a Compose file and what the agent makes of it.</summary>
/// <param name="Number">Its number in the file, from 1.</param>
/// <param name="Text">The line as written.</param>
/// <param name="Verdict">One of the constants below; empty for a line that says nothing by itself (a blank, a comment, a key whose lines under it carry the verdicts).</param>
/// <param name="Note">What it becomes, or why it cannot be given; empty when the verdict says it all.</param>
public sealed record ComposeLine(int Number, string Text, string Verdict, string Note)
{
    /// <summary>Taken as written.</summary>
    public const string Ok = "ok";

    /// <summary>Taken, as something else: a relative path made whole, a volume under the project's name, a duration in seconds.</summary>
    public const string Converted = "converted";

    /// <summary>Asked for and not given: WSLC has no equivalent, or the agent does not send it yet.</summary>
    public const string Unsupported = "unsupported";

    /// <summary>Asked for and already there, or with nothing to do here.</summary>
    public const string NotNeeded = "notNeeded";

    /// <summary>Read with a doubt: a variable that is not set, a file that is not there, a service its profiles leave out.</summary>
    public const string Warning = "warning";
}

/// <summary>One service of a Compose file.</summary>
/// <param name="Name">Its name in the file, which is how the others reach it.</param>
/// <param name="Launch">The container it becomes, as the Run form would send it.</param>
/// <param name="DependsOn">The services it waits for.</param>
/// <param name="Build">How its image is built, when the file says so; null for an image that is pulled.</param>
/// <param name="Profiles">The profiles it belongs to; none means always.</param>
/// <param name="Enabled">Whether it would be started with the profiles asked for.</param>
public sealed record ComposeService(
    string Name,
    ContainerLaunchRequest Launch,
    IReadOnlyList<ComposeDependency> DependsOn,
    ComposeBuild? Build,
    IReadOnlyList<string> Profiles,
    bool Enabled);

/// <summary>A service another one waits for, and for what: <c>service_started</c>, <c>service_healthy</c> or <c>service_completed_successfully</c>.</summary>
public sealed record ComposeDependency(string Service, string Condition)
{
    public const string Started = "service_started";
    public const string Healthy = "service_healthy";
    public const string Completed = "service_completed_successfully";
}

/// <summary>A service's <c>build</c>: the context folder, and the Dockerfile, target stage and arguments when given.</summary>
public sealed record ComposeBuild(string Context, string Dockerfile, string Target, IReadOnlyList<string> Args);

/// <summary>A network of the project.</summary>
/// <param name="Key">Its name in the file.</param>
/// <param name="Name">Its name on the machine: <c>project_key</c>, or the one the file gives.</param>
/// <param name="External">It exists already and is not the project's to create or remove.</param>
public sealed record ComposeNetwork(string Key, string Name, string Subnet, string Gateway, bool Internal, bool External);

/// <summary>A named volume of the project.</summary>
/// <param name="Key">Its name in the file.</param>
/// <param name="Name">Its name on the machine: <c>project_key</c>, or the one the file gives.</param>
/// <param name="External">It exists already and is not the project's to create or remove.</param>
public sealed record ComposeVolume(string Key, string Name, string Driver, IReadOnlyList<string> Options, bool External);
