namespace Berpiztu.Dashboard.Storage;

/// <summary>
/// The dashboard's files, as the application keeps them: how each kind of
/// object is born and the default dashboard, each a file with a version of its
/// own and the oldest application that loads it; the user's dashboard against
/// the release and the installation's own default, view by view; every state
/// it was saved in, any of which is brought back; and the dashboard carried to
/// another installation. The dashboard's Tools (<c>DashboardTools</c>) shows
/// and drives them; where they are kept, and how a file reaches the user, is
/// the application's.
/// </summary>
public interface IDashboardFiles
{
    /// <summary>The two files as this installation has them, the user's dashboard against them, and the views a file can carry.</summary>
    Task<DashboardFilesStatus> StatusAsync(CancellationToken cancellationToken = default);

    /// <summary>What a file holds, and whether it can be loaded here, before it is.</summary>
    Task<DashboardFileInfo> InspectAsync(string file, CancellationToken cancellationToken = default);

    /// <summary>The objects' defaults of a file, or of the one waiting to be loaded when <paramref name="file"/> is empty, the base from now on.</summary>
    Task<DashboardFilesStatus> LoadObjectDefaultsAsync(string file, CancellationToken cancellationToken = default);

    /// <summary>The objects' defaults the application ships, the base again; the user's changes stay over it.</summary>
    Task<DashboardFilesStatus> ResetObjectDefaultsAsync(CancellationToken cancellationToken = default);

    /// <summary>Every change the user made to the objects' defaults dropped, backed up first: the base alone again.</summary>
    Task<DashboardFilesStatus> ClearChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Conflicts between the user's changes and a newer base settled, kind by kind.</summary>
    Task<DashboardFilesStatus> SettleConflictsAsync(IReadOnlyList<DashboardConflictChoice> choices, CancellationToken cancellationToken = default);

    /// <summary>The objects' defaults in use, as a file another installation loads.</summary>
    Task<string> ExportObjectDefaultsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The views named of a dashboard file, or of the release when
    /// <paramref name="file"/> is empty, in place of the user's same views; the
    /// dashboard starts again from that version.
    /// </summary>
    Task<DashboardFilesStatus> LoadViewsAsync(IReadOnlyCollection<string> views, string file, CancellationToken cancellationToken = default);

    /// <summary>The views named of the user's dashboard put back to the default this installation has.</summary>
    Task<DashboardFilesStatus> ResetViewsAsync(IReadOnlyCollection<string> views, CancellationToken cancellationToken = default);

    /// <summary>The whole dashboard back to the release, every view at once.</summary>
    Task<DashboardFilesStatus> ResetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Every state the dashboard was saved in, the newest first.</summary>
    Task<IReadOnlyList<DashboardRevisionInfo>> HistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>A state the dashboard was saved in brought back, as it was.</summary>
    Task<DashboardFilesStatus> RestoreAsync(int revision, CancellationToken cancellationToken = default);

    /// <summary>
    /// The views named made this installation's own default — what a user
    /// with none is given and Reset puts back — from a file, or the release's
    /// when <paramref name="file"/> is empty; the user's dashboard is not touched.
    /// </summary>
    Task<DashboardFilesStatus> LoadDefaultViewsAsync(IReadOnlyCollection<string> views, string file, CancellationToken cancellationToken = default);

    /// <summary>The views named of this installation's default back to the shipped ones.</summary>
    Task<DashboardFilesStatus> ResetDefaultViewsAsync(IReadOnlyCollection<string> views, CancellationToken cancellationToken = default);

    /// <summary>The views named of the user's dashboard, as a file another installation loads.</summary>
    Task<string> ExportViewsAsync(IReadOnlyCollection<string> views, CancellationToken cancellationToken = default);

    /// <summary>A file handed to the user, as the application hands files: a download, a save dialog.</summary>
    Task SaveAsync(string name, string text, CancellationToken cancellationToken = default);
}

/// <summary>A file's identity: its own version, which only grows, and the oldest application that loads it.</summary>
public sealed record DashboardFileVersion(int Version, string MinVersion);

/// <summary>
/// One of the two files: the version shipped, the one loaded as the base
/// (null while the shipped one is), the one waiting to be loaded (null for
/// none), whether that one is newer, why it cannot be loaded here (empty when
/// it can), the dashboard views it holds; and, for the objects' defaults, how
/// many kinds the user changed over the base, and those whose base changed
/// too since, to settle.
/// </summary>
public sealed record DashboardFileState(
    DashboardFileVersion Shipped,
    DashboardFileVersion? Loaded,
    DashboardFileVersion? Waiting,
    bool Newer,
    string Refusal,
    IReadOnlyList<string> WaitingViews,
    int Changes,
    IReadOnlyList<DashboardConflict> Conflicts);

/// <summary>A kind the user changed whose base has changed since: in which view, and the object type.</summary>
public sealed record DashboardConflict(string View, string Type);

/// <summary>How a conflict is settled: the user's change kept, over the base as it is now, or the base's taken.</summary>
public sealed record DashboardConflictChoice(string View, string Type, bool KeepMine);

/// <summary>A dashboard view a file names: its id (<c>system-landscape</c>) and how it is shown.</summary>
public sealed record DashboardViewName(string Id, string Title);

/// <summary>The two files, the user's dashboard against them, and every view a dashboard file can carry, in the order they are listed.</summary>
public sealed record DashboardFilesStatus(
    DashboardFileState ObjectDefaults,
    DashboardFileState DashboardFile,
    DashboardViewsState Dashboard,
    IReadOnlyList<DashboardViewName> Views);

/// <summary>The user's dashboard: where it comes from now (v2.3), the release it is compared with, and each view against it.</summary>
public sealed record DashboardViewsState(
    DashboardRevisionInfo? Current,
    int ReleaseVersion,
    string ReleaseSource,
    IReadOnlyList<DashboardViewDiff> Views);

/// <summary>
/// One view of the user's dashboard against the release, and, where the
/// installation has a default of its own for it, against that default and
/// that default against the release.
/// </summary>
public sealed record DashboardViewDiff(
    string View,
    bool OwnDefault,
    int OwnBasedOn,
    IReadOnlyList<DashboardDifference> FromRelease,
    IReadOnlyList<DashboardDifference> FromOwnDefault,
    IReadOnlyList<DashboardDifference> OwnFromRelease);

/// <summary>One difference: the object or card, added, removed or changed, and what.</summary>
public sealed record DashboardDifference(string Object, string Change, string Detail);

/// <summary>A state the dashboard was saved in: its id, its origin (version, from the installation or an import, its name), its revision within it, when, and why.</summary>
public sealed record DashboardRevisionInfo(int Id, int Version, int Revision, string Source, string Name, string Saved, string Note);

/// <summary>What a file holds: a dashboard (else the objects' defaults), its version and revision, the oldest application it needs, where and when it was made, its views, and why it cannot be loaded here (empty when it can).</summary>
public sealed record DashboardFileInfo(
    bool IsDashboard,
    int Version,
    int Revision,
    string MinVersion,
    string From,
    string Created,
    IReadOnlyList<string> Views,
    string Refusal);

/// <summary>A request about the dashboard's files that failed or was refused, with the reason the user is shown.</summary>
public sealed class DashboardFilesException(string message, Exception? inner = null) : Exception(message, inner);
