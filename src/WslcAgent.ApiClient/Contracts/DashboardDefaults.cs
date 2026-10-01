namespace WslcAgent.ApiClient.Contracts;

/// <summary>A defaults file's identity: its own version, which only grows, and the oldest agent that loads it.</summary>
public sealed record DefaultsFileVersion(int Version, string MinAgentVersion);

/// <summary>
/// One of the two files the agent ships, the objects' defaults or the default
/// dashboard (docs/dashboard-defaults.md): the version this agent ships, the
/// one loaded from outside it, and the one waiting in the package folder.
/// </summary>
/// <param name="Kind"><see cref="DefaultsKinds.ObjectDefaults"/> or <see cref="DefaultsKinds.Dashboard"/>.</param>
/// <param name="Shipped">The version embedded in this agent.</param>
/// <param name="Loaded">The version last loaded from a file; null while the shipped one is the one in use.</param>
/// <param name="Available">The version in the package folder; null when there is none.</param>
/// <param name="Newer">The package folder's is newer than the one in use.</param>
/// <param name="Refusal">Why the package folder's cannot be loaded here (an older agent than it needs); empty when it can.</param>
/// <param name="AvailableViews">The package folder's dashboard views that hold objects (<see cref="DashboardViewNames"/>); empty for object defaults.</param>
/// <param name="Changes">The objects' defaults the user changed on this agent, kept over the base; 0 for the dashboard.</param>
/// <param name="Conflicts">The kinds the user changed whose base has changed since, to settle; empty for the dashboard.</param>
public sealed record DefaultsFileState(
    string Kind,
    DefaultsFileVersion Shipped,
    DefaultsFileVersion? Loaded,
    DefaultsFileVersion? Available,
    bool Newer,
    string Refusal,
    IReadOnlyList<string> AvailableViews,
    int Changes,
    IReadOnlyList<ObjectDefaultConflict> Conflicts);

/// <summary>Body of <c>GET /api/v1/dashboard/defaults</c>: the two shipped files, and the user's dashboard against them.</summary>
public sealed record DashboardDefaultsStatus(
    string AgentVersion,
    string PackageFolder,
    DefaultsFileState ObjectDefaults,
    DefaultsFileState DashboardFile,
    DashboardState Dashboard);

/// <summary>
/// The user's dashboard against the default: where it comes from, the release
/// it is compared with — the installation's, or the package folder's when that
/// one is newer and this agent loads it — and each view against it.
/// </summary>
/// <param name="Current">The dashboard's origin and revision now; null before anything was recorded.</param>
/// <param name="ReleaseVersion">The version of the default the views are compared with.</param>
/// <param name="ReleaseSource"><c>installation</c>, or <c>package folder</c> when the waiting file is the newer one.</param>
public sealed record DashboardState(
    DashboardRevision? Current,
    int ReleaseVersion,
    string ReleaseSource,
    IReadOnlyList<DashboardViewState> Views);

/// <summary>
/// One view of the user's dashboard against the release and against this
/// agent's own default, JSON against JSON.
/// </summary>
/// <param name="OwnDefault">This agent has a default of its own for the view.</param>
/// <param name="OwnBasedOn">The version of the default the agent's own view was based on; 0 when it has none.</param>
/// <param name="FromRelease">What the user's view changes from the release's.</param>
/// <param name="FromOwnDefault">What the user's view changes from the agent's own default view; empty when there is none.</param>
/// <param name="OwnFromRelease">What the agent's own default view changes from the release's; empty when there is none.</param>
public sealed record DashboardViewState(
    string View,
    bool OwnDefault,
    int OwnBasedOn,
    IReadOnlyList<DashboardChange> FromRelease,
    IReadOnlyList<DashboardChange> FromOwnDefault,
    IReadOnlyList<DashboardChange> OwnFromRelease);

/// <summary>One difference between two dashboards in a view: the object or card, <c>added</c>, <c>removed</c> or <c>changed</c>, and what.</summary>
public sealed record DashboardChange(string Object, string Change, string Detail);

/// <summary>
/// A state the user's dashboard was saved in (<c>GET /api/v1/me/dashboard-v2/history</c>):
/// its origin — the version, from the <c>installation</c> or an <c>import</c>
/// (its name) — and its revision within it (v2.3), when, and why.
/// </summary>
public sealed record DashboardRevision(int Id, int Version, int Revision, string Source, string Name, string Saved, string Note);

/// <summary>Body of <c>POST /api/v1/dashboard/defaults/inspect</c>: what a defaults file holds, before it is loaded.</summary>
/// <param name="Kind"><see cref="DefaultsKinds.ObjectDefaults"/> or <see cref="DefaultsKinds.Dashboard"/>.</param>
/// <param name="Revision">An export's revision within its version (v2.3); 0 for a file as released.</param>
/// <param name="From">The agent that exported it; empty for a shipped file.</param>
/// <param name="Created">When it was written, ISO 8601; empty when it does not say.</param>
/// <param name="Views">A dashboard's views that hold objects (<see cref="DashboardViewNames"/>); empty for object defaults.</param>
/// <param name="Refusal">Why this agent cannot load it; empty when it can.</param>
public sealed record DefaultsFileInfo(
    string Kind,
    int Version,
    int Revision,
    string MinAgentVersion,
    string From,
    string Created,
    IReadOnlyList<string> Views,
    string Refusal);

/// <summary>A kind the user changed whose base has changed since: in which view, and the object type.</summary>
public sealed record ObjectDefaultConflict(string View, string Type);

/// <summary>How a conflict is settled: the user's change kept, over the base as it is now, or the base's taken.</summary>
public sealed record ObjectDefaultChoice(string View, string Type, bool KeepMine);

/// <summary>The two kinds of defaults file.</summary>
public static class DefaultsKinds
{
    public const string ObjectDefaults = "object-defaults";

    public const string Dashboard = "dashboard";
}

/// <summary>A dashboard's views as a file names them: the page, then landscape or portrait.</summary>
public static class DashboardViewNames
{
    public const string SystemLandscape = "system-landscape";

    public const string SystemPortrait = "system-portrait";

    public const string UserLandscape = "user-landscape";

    public const string UserPortrait = "user-portrait";

    /// <summary>The four, in the order Settings lists them.</summary>
    public static IReadOnlyList<string> All { get; } = [SystemLandscape, SystemPortrait, UserLandscape, UserPortrait];

    /// <summary>How a view is shown: "System landscape".</summary>
    public static string Title(string view) =>
        view.Length > 0 ? char.ToUpperInvariant(view[0]) + view[1..].Replace('-', ' ') : view;
}
