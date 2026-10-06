using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Overview;

/// <summary>
/// The Home dashboard, its landscape and portrait views, kept with the user
/// in the agent's data folder (<c>dashboard-v2.5.json</c>), so every client
/// opens the same one. It keeps the client's text as it was written and reads
/// nothing inside it, and a user who has none yet is given the default the
/// agent ships with (embedded, <c>dashboard-v2.5.default.json</c>), written
/// to their file the first time it is asked for. Every state it is saved in is
/// kept, with its origin and revision (<see cref="DashboardHistory"/>).
/// <para>
/// The file is shared with any other agent on the machine (an installed one
/// and a development one): it is read again whenever another has written it,
/// and changed under the lock they all take (<see cref="SharedDataFile"/>).
/// Kept as it was read at the start, one agent served a dashboard hours old
/// and wrote it back over what was saved through the other.
/// </para>
/// <para>
/// A development build writes the default back to the repository, raising its
/// version, so the next installer ships what was designed. An installed agent
/// keeps a default of its own instead, view by view over the shipped one, which
/// it never touches: what a user with none is given and what Reset puts back.
/// </para>
/// </summary>
public sealed class DashboardV2Store
{
    private readonly ShippedFile _default = new("dashboard-v2.5.default.json", "DashboardV2DefaultSource");
    private readonly SharedDataFile _file;
    private readonly Lock _gate = new();
    private readonly LoadedDefaults _loaded;
    private readonly DefaultsVersions _versions;
    private readonly DashboardHistory _history;
    private string? _layout;

    public DashboardV2Store(IOptions<WslcOptions> options, LoadedDefaults loaded, DefaultsVersions versions, DashboardHistory history, ILogger<DashboardV2Store> logger)
    {
        _loaded = loaded;
        _versions = versions;
        _history = history;
        _file = new SharedDataFile(Path.Combine(options.Value.DataDirectory, "dashboard-v2.5.json"), logger);
        Refresh();
    }

    /// <summary>The version of the default dashboard this agent ships.</summary>
    public int ShippedVersion => _versions.For(DefaultsKinds.Dashboard).Version;

    /// <summary>What the user has; the default, kept as theirs, the first time they have none; null when there is no default either.</summary>
    public string? Get()
    {
        lock (_gate)
        {
            Refresh();
            if (_layout is not null)
            {
                return _layout;
            }

            // Given the default only where there is truly no file: one that
            // could not be read just now is not a user without a dashboard.
            using var held = _file.Lock();
            Refresh();
            if (_layout is null && !_file.WrittenSinceRead && Default() is { } fallback)
            {
                _file.Write(fallback);
                _layout = fallback;
                _history.Record(fallback, new DashboardOrigin(ShippedVersion, DashboardHistory.Installation, ""), "Given the default");
            }

            return _layout;
        }
    }

    /// <summary>What the client wrote, kept as it was written and recorded as the next revision; an empty body forgets it.</summary>
    public void Set(string? layout) => Set(layout, null, "Saved");

    /// <summary>
    /// The dashboard replaced and recorded: from a new <paramref name="origin"/>,
    /// as its revision 0 (or <paramref name="revision"/>, a revision brought
    /// back), or — null — as the next revision of the one it has.
    /// </summary>
    public void Set(string? layout, DashboardOrigin? origin, string note, int? revision = null)
    {
        lock (_gate)
        {
            using var held = _file.Lock();
            Refresh();
            KeepWhatIsReplaced(note);
            _layout = string.IsNullOrWhiteSpace(layout) ? null : layout;
            if (_layout is null)
            {
                _file.Delete();
                return;
            }

            _file.Write(_layout);
            _history.Record(_layout, origin, note, revision);
        }
    }

    /// <summary>
    /// The file again, when another agent has written it since it was read. One
    /// that cannot be opened at that moment leaves what was read before, and is
    /// tried again at the next look: never taken for a dashboard that is not
    /// there, which would give the user the default over their own.
    /// </summary>
    private void Refresh()
    {
        if (!_file.WrittenSinceRead)
        {
            return;
        }

        try
        {
            _layout = _file.Read() is { Length: > 0 } text ? text : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // What was read before stays.
        }
    }

    /// <summary>
    /// The dashboard about to be replaced recorded first, when the history
    /// does not hold it as its newest: one saved before the history was kept,
    /// or written by hand. Nothing the user had is lost to a reset, a restore
    /// or a load (a reset to the release once wrote over a whole dashboard
    /// that was nowhere else); Restore brings it back.
    /// </summary>
    private void KeepWhatIsReplaced(string note)
    {
        try
        {
            if (_layout is { } replaced && !_history.NewestIs(replaced))
            {
                _history.Record(replaced, null, $"Kept before: {note}");
            }
        }
        catch (JsonException)
        {
            // Text that is not a dashboard (a file broken by hand) shows nothing
            // and cannot be brought back; the change goes on without it.
        }
    }

    /// <summary>
    /// The default a user with none is given, and a blank view shows: the one
    /// the agent ships, with this agent's own views over it (saved as default
    /// on it, or loaded into its default); null when there is none.
    /// </summary>
    public string? Default()
    {
        var shipped = _default.Read();
        var (own, views) = _loaded.ReadOwnDefault();
        return _default.Writable || views.Count == 0 ? shipped : StoredDashboardViews.Merge(Parse(shipped), own, [.. views.Keys]).ToJsonString();
    }

    /// <summary>The default as the agent ships it, without this agent's own views; null when there is none.</summary>
    public string? ShippedDefault() => _default.Read();

    /// <summary>This agent's own default views, each with the version of the default it was based on; empty on a development build.</summary>
    public IReadOnlyDictionary<string, int> OwnDefaultViews => _default.Writable ? new Dictionary<string, int>() : _loaded.ReadOwnDefault().Views;

    /// <summary>This agent's own default, its views only; empty when it has none.</summary>
    public JsonObject OwnDefault() => _loaded.ReadOwnDefault().Content;

    /// <summary>Whether this agent writes the default back to the repository (a development build).</summary>
    public bool DefaultWritable => _default.Writable;

    /// <summary>
    /// The views named of <paramref name="from"/> made this agent's own default,
    /// based on version <paramref name="basedOn"/> of the default; the default it
    /// had backed up first.
    /// </summary>
    public void SetOwnDefault(JsonObject from, IReadOnlyCollection<string> views, int basedOn)
    {
        lock (_gate)
        {
            var (own, record) = _loaded.ReadOwnDefault();
            var versions = record.ToDictionary();
            foreach (var view in views)
            {
                versions[view] = basedOn;
            }

            _loaded.WriteOwnDefault(StoredDashboardViews.Merge(own, from, views), versions);
        }
    }

    /// <summary>The views named back to the default the agent ships: this agent's own ones dropped, backed up first.</summary>
    public void ResetOwnDefault(IReadOnlyCollection<string> views)
    {
        lock (_gate)
        {
            var (own, record) = _loaded.ReadOwnDefault();
            _loaded.WriteOwnDefault(StoredDashboardViews.Remove(own, views), record.Where(view => !views.Contains(view.Key)).ToDictionary());
        }
    }

    /// <summary>
    /// The default as a client wrote it, from Save as default: on a development
    /// build written back to the repository, one version up; on an installed
    /// agent, the views it changed made this agent's own default, over the
    /// shipped one, which it never touches.
    /// </summary>
    public void SetDefault(string layout)
    {
        var after = Parse(layout);
        if (_default.Writable)
        {
            _default.Write(layout);
            _versions.RaiseDashboard();
            return;
        }

        var before = Parse(Default());
        SetOwnDefault(after, [.. DashboardViewNames.All.Where(view => !StoredDashboardViews.Same(before, after, view))], ShippedVersion);
    }

    private static JsonObject Parse(string? text)
    {
        try
        {
            return text is not null && JsonNode.Parse(text) is JsonObject layout ? layout : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
