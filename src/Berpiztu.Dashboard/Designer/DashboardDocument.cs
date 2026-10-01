using Berpiztu.Dashboard.Catalogue;
using Berpiztu.Dashboard.Model;
using Berpiztu.Dashboard.Storage;

namespace Berpiztu.Dashboard.Designer;

/// <summary>
/// The dashboard being shown or designed: its layout, what is selected — an
/// object, or a group taken whole — and the one change that can be taken
/// back.
/// <para>
/// Given no draft, every change is kept at once — there is no Save — so what
/// is on the screen is what the
/// store holds: the board of every object's defaults. Given a draft, nothing
/// reaches the store without Save: each change is kept in the draft, on the device, so a
/// power cut or a lost connection loses nothing, and the draft is what is
/// designed until it is saved or discarded.
/// </para>
/// </summary>
/// <param name="store">Where the dashboard is kept.</param>
/// <param name="cardOnly">
/// Whether a kind of object lives only in its card (<see cref="DashboardObjectAttribute.CardOnly"/>):
/// such an object never leaves it, its card takes no other and is never
/// ungrouped, and removing it hides it.
/// </param>
/// <param name="draft">Where what is designed is kept until Save; none, and every change is kept in the store at once.</param>
public sealed class DashboardDocument(IDashboardStore store, Func<string, bool>? cardOnly = null, IDashboardStore? draft = null)
{
    private readonly SemaphoreSlim _saving = new(1, 1);

    /// <summary>The layout before the last change, for undo; one step only.</summary>
    private DashboardLayout? _before;

    /// <summary>The layout as the store holds it; the one on the screen until a change is made with a draft.</summary>
    private DashboardLayout _saved = DashboardLayout.Empty;

    public DashboardLayout Layout { get; private set; } = DashboardLayout.Empty;

    /// <summary>What is designed is not what the store holds: Save and Discard have something to do, and leaving design asks first.</summary>
    public bool Unsaved => !ReferenceEquals(Layout, _saved);

    /// <summary>
    /// What is chosen, objects and groups alike: one object, whose properties
    /// the window shows; one group, taken whole as a card is; or several of either, chosen with Ctrl or
    /// a drawn rectangle, to be moved together, made one group, or removed.
    /// </summary>
    private readonly List<string> _chosen = [];

    /// <summary>The ids chosen, objects and groups, in the order they were chosen.</summary>
    public IReadOnlyList<string> Chosen => _chosen;

    /// <summary>The id of the one object chosen, whose properties the window shows; null when none is, several are, or a group is.</summary>
    public string? Selected => _chosen.Count == 1 && Layout.Find(_chosen[0]) is not null ? _chosen[0] : null;

    /// <summary>The id of the one group chosen, taken whole; null when none is, or anything else is chosen with it.</summary>
    public string? SelectedGroup => _chosen.Count == 1 && Layout.FindGroup(_chosen[0]) is not null ? _chosen[0] : null;

    public ObjectInstance? SelectedObject => Selected is { } id ? Layout.Find(id) : null;

    public DashboardGroup? SelectedFrame => Layout.FindGroup(SelectedGroup);

    /// <summary>The objects chosen, as the dashboard holds them.</summary>
    public IReadOnlyList<ObjectInstance> ChosenObjects => [.. _chosen.Select(Layout.Find).OfType<ObjectInstance>()];

    /// <summary>The groups chosen, as the dashboard holds them.</summary>
    public IReadOnlyList<DashboardGroup> ChosenGroups => [.. _chosen.Select(Layout.FindGroup).OfType<DashboardGroup>()];

    /// <summary>Several chosen, objects standing alone or groups: what Group makes one group of; a card whose pieces live only in it melts into no other.</summary>
    public bool CanGroup => _chosen.Count >= 2 && ChosenObjects.All(o => o.Group is null && !CardOnly(o)) && !ChosenGroups.Any(Closed);

    /// <summary>The groups chosen that can be ungrouped: every one but a card whose pieces live only in it.</summary>
    public bool CanUngroup => ChosenGroups.Any(group => !Closed(group));

    /// <summary>An object of a kind that lives only in its card.</summary>
    public bool CardOnly(ObjectInstance o) => cardOnly?.Invoke(o.Type) == true;

    /// <summary>A card holding a piece that lives only in it, which takes no other object.</summary>
    public bool Closed(DashboardGroup group) => Layout.Members(group.Id).Any(CardOnly);

    public bool CanUndo => _before is not null;

    /// <summary>Anything shown changed: the layout, the selection, whether undo can be pressed.</summary>
    public event Action? Changed;

    /// <summary>A save that did not reach the store; the change stays on the screen and the next one tries again.</summary>
    public event Action<Exception>? SaveFailed;

    /// <summary>The dashboard as the store holds it, and nothing undone; a draft left on the device is opened only in design (<see cref="OpenDraftAsync"/>).</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _saved = DashboardLayout.Read(await store.LoadAsync(cancellationToken));
        Layout = _saved;
        _before = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// Design begins: a draft left on the device — by a power cut, a lost
    /// connection, a window closed — is what is designed again, unsaved.
    /// </summary>
    public async Task OpenDraftAsync(CancellationToken cancellationToken = default)
    {
        if (draft is null || Unsaved || await draft.LoadAsync(cancellationToken) is not { Length: > 0 } kept)
        {
            return;
        }

        Layout = DashboardLayout.Read(kept) with { Columns = Layout.Columns };
        _before = null;
        LetGoOfWhatIsGone();
        Changed?.Invoke();
    }

    /// <summary>
    /// Save: what is designed written to the store, and the draft let go.
    /// Undo stays, so a change saved can be taken back and saved again. False,
    /// and the design kept on the screen and in the draft, where the store
    /// could not be reached.
    /// </summary>
    public async Task<bool> SaveAsync()
    {
        if (draft is null || !Unsaved)
        {
            return true;
        }

        var layout = Layout;
        await _saving.WaitAsync();
        try
        {
            await store.SaveAsync(layout.Write());
            _saved = layout;
            await KeepDraftAsync(draft);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SaveFailed?.Invoke(ex);
            return false;
        }
        finally
        {
            _saving.Release();
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>Discard: the dashboard back as the store holds it, the draft let go, and nothing to undo.</summary>
    public async Task DiscardAsync()
    {
        if (!Unsaved)
        {
            return;
        }

        Layout = _saved;
        _before = null;
        LetGoOfWhatIsGone();
        Changed?.Invoke();
        await KeepAsync();
    }

    /// <summary>One object or group chosen, or nothing; whatever else was chosen lets go.</summary>
    public void Select(string? id) => Choose(id is null ? [] : [id]);

    /// <summary>A group taken whole, as a card is by its first tap.</summary>
    public void SelectGroup(string id) => Choose([id]);

    /// <summary>These chosen, and nothing else: a drawn rectangle's catch.</summary>
    public void Choose(IEnumerable<string> ids)
    {
        _chosen.Clear();
        _chosen.AddRange(ids.Distinct());
        Changed?.Invoke();
    }

    /// <summary>
    /// Ctrl with a press: what it pressed joins what is chosen, or leaves it if
    /// it was there. Several are chosen on one level: objects standing alone
    /// and whole groups together, or objects of one group together; what was chosen on another level lets go first.
    /// </summary>
    public void Toggle(string id)
    {
        var level = Layout.Find(id)?.Group;
        if (_chosen.Any(chosen => Layout.Find(chosen)?.Group != level))
        {
            _chosen.Clear();
        }

        if (!_chosen.Remove(id))
        {
            _chosen.Add(id);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// One object as it should now stand or look. Refused — false, nothing
    /// changed — when it would leave the canvas, take another object's cells,
    /// or leave its group's frame (or, standing alone, enter one).
    /// </summary>
    public Task<bool> SetAsync(ObjectInstance changed) =>
        Layout.Find(changed.Id) == changed ? Task.FromResult(true)
        : Layout.Fits(changed) ? ChangeAsync(Layout.With(changed))
        : Task.FromResult(false);

    /// <summary>
    /// A card made already dropped at a cell (a template): its objects in their places, framed as one group,
    /// added in one change and the group taken, so what it reads is chosen
    /// once for them all. Refused where the card would not fit.
    /// </summary>
    /// <param name="card">The card as it is born (<see cref="DashboardTemplates.Born"/>): its objects from its top-left cell, and its size.</param>
    public async Task<bool> AddCardAsync(BornCard card, int x, int y)
    {
        var groupId = Guid.NewGuid().ToString("N");
        IReadOnlyList<ObjectInstance> members =
            [.. card.Objects.Select(o => o with { Id = Guid.NewGuid().ToString("N"), X = x + o.X, Y = y + o.Y, Group = groupId })];
        var group = card.Framed(groupId, x, y);
        if (!Layout.FitsGroup(group, members))
        {
            return false;
        }

        await ChangeAsync(Layout.With(group, members));
        SelectGroup(groupId);
        return true;
    }

    /// <summary>The alarms the status bar shows, as ticked in the properties window, in one change undo takes back.</summary>
    public Task SetStatusAsync(IReadOnlyList<StatusAlarm> status) => ChangeAsync(Layout with { Status = status });

    /// <summary>The view's size in cells, set in design, in one change undo takes back; a size under one cell is not a size, and changes nothing.</summary>
    public Task SetCanvasSizeAsync(int width, int height) =>
        width < 1 || height < 1 || (Layout.CanvasWidth == width && Layout.CanvasHeight == height)
            ? Task.CompletedTask
            : ChangeAsync(Layout with { CanvasWidth = width, CanvasHeight = height });

    /// <summary>Several objects changed at once, in one change undo takes back whole: a card's source, chosen once for its objects.</summary>
    public Task SetEachAsync(IReadOnlyList<ObjectInstance> changed) =>
        changed.Count == 0 ? Task.CompletedTask : ChangeAsync(changed.Aggregate(Layout, (layout, o) => layout.With(o)));

    /// <summary>
    /// Several objects changed at once where every one of them fits as it
    /// would then stand, in one change; refused whole, and nothing changed,
    /// where one would not.
    /// </summary>
    public async Task<bool> SetEachWhereTheyFitAsync(IReadOnlyList<ObjectInstance> changed)
    {
        var next = changed.Aggregate(Layout, (layout, o) => layout.With(o));
        if (!changed.All(next.Fits))
        {
            return false;
        }

        await SetEachAsync(changed);
        return true;
    }

    /// <summary>Dropped from the toolbox: added, and selected so its properties show at once.</summary>
    public async Task<bool> AddAsync(ObjectInstance dropped)
    {
        if (!await SetAsync(dropped))
        {
            return false;
        }

        Select(dropped.Id);
        return true;
    }

    /// <summary>
    /// <paramref name="dropped"/> let go on <paramref name="target"/>, both
    /// standing alone: the two become a group.
    /// The target stays; the one dropped goes beside it (DashboardLayout.Beside),
    /// and a frame is drawn round the two, taken whole at once. Refused —
    /// false, nothing changed — when there is no room beside the target.
    /// </summary>
    public async Task<bool> GroupAsync(ObjectInstance target, ObjectInstance dropped)
    {
        if (target.Group is not null || CardOnly(target) || CardOnly(dropped) || Layout.Beside(target, dropped) is not { } placed)
        {
            return false;
        }

        var id = Guid.NewGuid().ToString("N");
        var frame = CellBox.Around([target.Box, placed.Box]);
        await ChangeAsync(Layout.With(new DashboardGroup(id, frame.X, frame.Y, frame.W, frame.H), [target with { Group = id }, placed with { Group = id }]));
        SelectGroup(id);
        return true;
    }

    /// <summary>
    /// An object standing alone, carried into a group's frame and let go
    /// there: it joins the group where the user put it —<paramref name="placed"/> is it at that place, as a member — and
    /// the group is taken whole. Refused — false, nothing changed — where it
    /// would take cells another object stands on, or where the group is a card
    /// whose pieces live only in it.
    /// </summary>
    public async Task<bool> JoinAsync(ObjectInstance placed)
    {
        if (placed.Group is not { } groupId || Layout.FindGroup(groupId) is not { } group || Closed(group) || CardOnly(placed) || !Layout.Fits(placed))
        {
            return false;
        }

        await ChangeAsync(Layout.With(placed));
        SelectGroup(groupId);
        return true;
    }

    /// <summary>
    /// An object of a group carried out of its frame and let go: it leaves the group and stands alone where it was
    /// let go, and a group left with one object is a group no more — its
    /// frame goes and that object stands alone too. The frame keeps its size.
    /// Refused — false, nothing changed — where
    /// it would not fit standing alone, or where it lives only in its card.
    /// </summary>
    public async Task<bool> LeaveAsync(ObjectInstance moved)
    {
        if (moved.Group is not null || CardOnly(moved) || Layout.Find(moved.Id) is not { Group: { } groupId } || !Layout.Fits(moved))
        {
            return false;
        }

        var next = Layout.With(moved);
        if (next.Members(groupId).Count <= 1)
        {
            next = next.Ungrouped(groupId);
        }

        await ChangeAsync(next);
        Select(moved.Id);
        return true;
    }

    /// <summary>
    /// A group moved or resized: its objects go with it by as much as its
    /// corner moved. Refused — false, nothing changed — when it would leave
    /// one of them out, or take cells that are not its own.
    /// </summary>
    public Task<bool> SetGroupAsync(DashboardGroup changed)
    {
        if (Layout.FindGroup(changed.Id) is not { } current)
        {
            return Task.FromResult(false);
        }

        if (current == changed)
        {
            return Task.FromResult(true);
        }

        var members = Moved(changed.Id, changed.X - current.X, changed.Y - current.Y);
        return Layout.FitsGroup(changed, members) ? ChangeAsync(Layout.With(changed, members)) : Task.FromResult(false);
    }

    /// <summary>The objects of a group as they stand once it is moved by <paramref name="dx"/>, <paramref name="dy"/>.</summary>
    public IReadOnlyList<ObjectInstance> Moved(string groupId, int dx, int dy) =>
        [.. Layout.Members(groupId).Select(member => member.MovedBy(dx, dy))];

    /// <summary>
    /// What is chosen, objects and groups, moved together by one drag: all of
    /// it, a group with its objects, or none of it when one would not fit. One
    /// change, so one undo takes it back.
    /// </summary>
    public Task<bool> MoveAllAsync(int dx, int dy)
    {
        var next = Layout.MovedTogether(_chosen, dx, dy);
        return next.FitsAll(_chosen) ? ChangeAsync(next) : Task.FromResult(false);
    }

    /// <summary>
    /// Group (Ctrl+G) with several chosen, objects standing alone and whole
    /// groups: one frame drawn round all of
    /// them, the groups chosen melted into it — no group holds another — and
    /// the group taken whole. Refused — false, nothing changed — when the frame
    /// would take the cells of anything else.
    /// </summary>
    public async Task<bool> GroupChosenAsync()
    {
        if (!CanGroup)
        {
            return false;
        }

        var id = Guid.NewGuid().ToString("N");
        var groups = ChosenGroups;
        var members = ChosenObjects.Concat(groups.SelectMany(g => Layout.Members(g.Id)))
            .Select(o => o with { Group = id })
            .ToList();
        var frame = CellBox.Around(members.Select(o => o.Box).Concat(groups.Select(g => g.Box)));
        var group = new DashboardGroup(id, frame.X, frame.Y, frame.W, frame.H);
        var next = groups.Aggregate(Layout, (layout, old) => layout.Ungrouped(old.Id)).With(group, members);
        if (!next.FitsGroup(group, members))
        {
            return false;
        }

        await ChangeAsync(next);
        SelectGroup(id);
        return true;
    }

    /// <summary>Ungroup (Ctrl+Shift+G) on the groups chosen: their frames go, and their objects stand alone where they are, chosen together; a card whose pieces live only in it stays whole.</summary>
    public async Task<bool> UngroupAsync()
    {
        var groups = ChosenGroups.Where(group => !Closed(group)).ToList();
        if (groups.Count == 0)
        {
            return false;
        }

        var members = groups.SelectMany(g => Layout.Members(g.Id)).Select(o => o.Id).ToList();
        await ChangeAsync(groups.Aggregate(Layout, (layout, group) => layout.Ungrouped(group.Id)));
        Choose(members);
        return true;
    }

    /// <summary>
    /// Everything chosen removed — objects, and groups with every object in
    /// them — in one change, so one undo brings it all back. A piece that lives
    /// only in its card is hidden instead, to be shown again; one found
    /// standing alone, with no card to be hidden in, is removed.
    /// </summary>
    public Task RemoveChosenAsync() =>
        ChangeAndKeepSelectionAsync(_chosen.Aggregate(Layout, (layout, id) =>
            layout.FindGroup(id) is not null ? layout.WithoutGroup(id)
            : layout.Find(id) is { Group: not null } o && CardOnly(o) ? layout.With(o with { Hidden = true })
            : layout.Without(id)));


    /// <summary>
    /// The objects whose source this agent does not have (a container
    /// deleted, or a dashboard loaded from another machine), by the source
    /// they were found without. Nothing is removed (the owner's decision of
    /// 1 October 2026): such an object stays in the dashboard, shown in
    /// design for the user to choose another source and left out of the view,
    /// where there is nothing it could show. It is known while the page lives,
    /// and choosing another source is what clears it.
    /// </summary>
    private readonly Dictionary<string, string?> _lost = [];

    /// <summary>An object says what it reads is not on this agent.</summary>
    public void Lose(ObjectInstance instance)
    {
        if (IsLost(instance))
        {
            return;
        }

        _lost[instance.Id] = instance.Source;
        Changed?.Invoke();
    }

    /// <summary>The object reads a source this agent does not have: the one it was found without, still chosen.</summary>
    public bool IsLost(ObjectInstance instance) =>
        _lost.TryGetValue(instance.Id, out var source) && source == instance.Source;

    /// <summary>
    /// The canvas shows this many columns now — as many cells as its width
    /// holds, and as many as its objects reach — which is what an object may
    /// be placed in. It follows the window, not the user, so it is neither
    /// saved on its own nor something undo takes back, nor a change to save.
    /// </summary>
    public void ShowColumns(int columns)
    {
        if (columns <= 0 || columns == Layout.Columns)
        {
            return;
        }

        var unsaved = Unsaved;
        _saved = _saved with { Columns = columns };
        Layout = unsaved ? Layout with { Columns = columns } : _saved;
    }

    /// <summary>The last change taken back; the selection stays where it was if that is still there.</summary>
    public async Task UndoAsync()
    {
        if (_before is not { } before)
        {
            return;
        }

        Layout = before;
        _before = null;
        LetGoOfWhatIsGone();
        Changed?.Invoke();
        await KeepAsync();
    }

    private async Task ChangeAndKeepSelectionAsync(DashboardLayout next)
    {
        await ChangeAsync(next);
        LetGoOfWhatIsGone();
        Changed?.Invoke();
    }

    /// <summary>A selection whose object or group is no longer on the dashboard is let go.</summary>
    private void LetGoOfWhatIsGone()
    {
        _chosen.RemoveAll(id => Layout.Find(id) is null && Layout.FindGroup(id) is null);
    }

    private async Task<bool> ChangeAsync(DashboardLayout next)
    {
        _before = Layout;
        Layout = next;
        Changed?.Invoke();
        await KeepAsync();
        return true;
    }

    /// <summary>
    /// What is designed kept, one write at a time, in order, so a later layout
    /// is never overwritten by an earlier one arriving late: in the draft
    /// where there is one, in the store otherwise.
    /// </summary>
    private async Task KeepAsync()
    {
        await _saving.WaitAsync();
        try
        {
            if (draft is not null)
            {
                await KeepDraftAsync(draft);
            }
            else
            {
                await store.SaveAsync(Layout.Write());
                _saved = Layout;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Whatever the application's store throws is the application's
            // (the SDK does not know its exceptions): it is reported, and the
            // layout stays on the screen for the next change to keep.
            SaveFailed?.Invoke(ex);
        }
        finally
        {
            _saving.Release();
        }
    }

    /// <summary>The draft following what is designed: written while it is not what the store holds, let go once it is.</summary>
    private Task KeepDraftAsync(IDashboardStore kept) => kept.SaveAsync(Unsaved ? Layout.Write() : "");
}
