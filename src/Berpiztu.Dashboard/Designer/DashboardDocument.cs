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
    /// <summary>The dashboard as it stood before each change, the last one last: what Undo takes back, one at a time.</summary>
    private readonly List<DashboardLayout> _before = [];

    /// <summary>How many changes Undo takes back, one after another.</summary>
    private const int UndoSteps = 100;

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

    /// <summary>
    /// Several chosen on one level (<see cref="LevelOf"/>) — objects standing
    /// alone and cards on the page, or what stands directly in one card, its
    /// pieces and the cards inside it — that Group puts in one group there.
    /// </summary>
    public bool CanGroup => _chosen.Count >= 2 && _chosen.Select(LevelOf).Distinct().Count() == 1;

    /// <summary>Group could not be done, and why, for the designer to say.</summary>
    public event Action<string>? Refused;

    /// <summary>The groups chosen that can be ungrouped: a group of pieces inside a card, its pieces going back to the card, and on the page every one but a card whose pieces live only in it.</summary>
    public bool CanUngroup => ChosenGroups.Any(Ungroupable);

    /// <summary>
    /// A group inside a card that the user or its template made of the card's
    /// pieces (no template of its own) is ungrouped back into the card; a card
    /// made already inside another (a container's in a project's) is not.
    /// </summary>
    private bool Ungroupable(DashboardGroup group) => group.Parent is null ? !Closed(group) : group.Template is null;

    /// <summary>An object of a kind that lives only in its card.</summary>
    public bool CardOnly(ObjectInstance o) => cardOnly?.Invoke(o.Type) == true;

    /// <summary>
    /// A card holding a piece that lives only in it: it is never ungrouped,
    /// and what it reads is what those pieces read. It takes other objects
    /// and cards as any group does; they stand in it, and leave it, as theirs.
    /// </summary>
    public bool Closed(DashboardGroup group) => Layout.Pieces(group.Id).Any(CardOnly);

    public bool CanUndo => _before.Count > 0;

    /// <summary>Anything shown changed: the layout, the selection, whether undo can be pressed.</summary>
    public event Action? Changed;

    /// <summary>A save that did not reach the store; the change stays on the screen and the next one tries again.</summary>
    public event Action<Exception>? SaveFailed;

    /// <summary>The dashboard as the store holds it, and nothing undone; a draft left on the device is opened only in design (<see cref="OpenDraftAsync"/>).</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _saved = DashboardLayout.Read(await store.LoadAsync(cancellationToken));
        Layout = _saved;
        _before.Clear();
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
        _before.Clear();
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
        _before.Clear();
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
    /// What an object or a card chosen stands directly in: an object's card, a
    /// card's own holder; null on the page. Several are chosen on one such
    /// level.
    /// </summary>
    public string? LevelOf(string id) => Layout.Find(id)?.Group ?? Layout.FindGroup(id)?.Parent;

    /// <summary>
    /// Ctrl with a press: what it pressed joins what is chosen, or leaves it if
    /// it was there. Several are chosen on one level (<see cref="LevelOf"/>):
    /// objects standing alone and cards on the page together, or what stands
    /// in one card together, its pieces and the cards inside it alike; what
    /// was chosen on another level lets go first.
    /// </summary>
    public void Toggle(string id)
    {
        var level = LevelOf(id);
        if (_chosen.Any(chosen => LevelOf(chosen) != level))
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
    /// <param name="parent">The card on the page it is dropped in, where it stands as a card of its own (<see cref="DashboardGroup.Parent"/>); null on the page.</param>
    public async Task<bool> AddCardAsync(BornCard card, int x, int y, string? parent = null)
    {
        var groupId = Guid.NewGuid().ToString("N");
        var group = card.Framed(groupId, x, y) with { Parent = parent };
        // Its groups of its own pieces, each under an id of its own, and each piece in its own.
        var inner = (card.Inner ?? []).Select(born => PiecesGroup(Guid.NewGuid().ToString("N"), born.Box with { X = x + born.X, Y = y + born.Y }, groupId)
            with { Anchor = born.Anchor, Holds = born.Holds }).ToList();
        var holderOf = (card.Inner ?? []).SelectMany((born, i) => born.Objects.Select(o => (o, inner[i].Id))).ToDictionary(pair => pair.o, pair => pair.Id);
        IReadOnlyList<ObjectInstance> members =
            [.. card.Objects.Select(o => o with { Id = Guid.NewGuid().ToString("N"), X = x + o.X, Y = y + o.Y, Group = holderOf.GetValueOrDefault(o.Id, groupId) })];
        if (!Layout.FitsGroup(group))
        {
            return false;
        }

        await ChangeAsync(inner.Aggregate(Layout.With(group, members), (layout, born) => layout.With(born, [])));
        SelectGroup(groupId);
        return true;
    }

    /// <summary>
    /// Cards made already put inside a group, one after another, each a card
    /// of its own in it (<see cref="DashboardLayout.WithInside"/>), each of
    /// its objects as <paramref name="each"/> makes it (what it reads is
    /// known already), all in one change undo takes back whole. The group
    /// stays selected: it is in its properties that they were asked for. How
    /// many found no room: the ones before them are put in all the same.
    /// </summary>
    public async Task<int> AddInsideAsync(string groupId, IReadOnlyList<BornCard> cards, Func<int, ObjectInstance, ObjectInstance> each, int perRow)
    {
        var (layout, none) = PlacedInside(Layout, groupId, cards, each, perRow);
        if (!ReferenceEquals(layout, Layout))
        {
            await ChangeAsync(layout);
        }

        return none;
    }

    /// <summary>
    /// What a card reads chosen anew, all at once or not at all: its pieces
    /// that read a source given it (<paramref name="readers"/>), the cards of
    /// what it read before taken out of it (<paramref name="gone"/>), and the
    /// cards of what it reads now put in <paramref name="place"/> — the card,
    /// or the group in it they go in. One change, so one undo takes it all
    /// back. Where they do not all fit, nothing is changed, and how many did
    /// not fit is answered.
    /// </summary>
    public async Task<int> ReadAnewAsync(IReadOnlyList<ObjectInstance> readers, string card, IReadOnlyCollection<string> gone, string place,
        IReadOnlyList<BornCard> cards, Func<int, ObjectInstance, ObjectInstance> each, int perRow)
    {
        var read = readers.Aggregate(Layout, (layout, reader) => layout.With(reader));
        var left = gone.Count == 0 ? read : read.WithoutInside(card, gone);
        var (next, none) = PlacedInside(left, place, cards, each, perRow);
        if (none > 0)
        {
            return none;
        }

        // A place set to shrink with its cards is as tall as the ones it holds now.
        await ChangeAndKeepSelectionAsync(next.Shrunk(place));
        return 0;
    }

    /// <summary>Cards made already put inside a group of <paramref name="layout"/>, one after another, as many as fit; with how many did not.</summary>
    private static (DashboardLayout Layout, int None) PlacedInside(DashboardLayout layout, string groupId, IReadOnlyList<BornCard> cards,
        Func<int, ObjectInstance, ObjectInstance> each, int perRow)
    {
        var none = 0;
        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            IReadOnlyList<ObjectInstance> objects = [.. card.Objects.Select(o => each(index, o with { Id = Guid.NewGuid().ToString("N") }))];
            if (layout.WithInside(groupId, card.Framed(Guid.NewGuid().ToString("N"), 0, 0), objects, perRow) is { } next)
            {
                layout = next;
            }
            else
            {
                none++;
            }
        }

        return (layout, none);
    }

    /// <summary>These cards of a group removed, each with its objects, and its frame drawn back to what is left, in one change undo takes back whole.</summary>
    public Task RemoveInsideAsync(string groupId, IReadOnlyCollection<string> cardIds)
    {
        if (cardIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        // The places in it set to shrink with their cards (AutoShrink) are as tall as what is left in them.
        var left = Layout.WithoutInside(groupId, cardIds);
        return ChangeAndKeepSelectionAsync(left.Descendants(groupId).Where(group => group.Holds is not null)
            .Aggregate(left, (layout, place) => layout.Shrunk(place.Id)));
    }

    /// <summary>The alarms the status bar shows, as ticked in the properties window, in one change undo takes back.</summary>
    public Task SetStatusAsync(IReadOnlyList<StatusAlarm> status) => ChangeAsync(Layout with { Status = status });

    /// <summary>How the page meets desktop and mobile screens, set in the properties window, in one change undo takes back.</summary>
    public Task SetScreensAsync(ViewScreens screens) => ChangeAsync(Layout with { Screens = screens });

    /// <summary>The view's size in cells, set in design, in one change undo takes back; a size under one cell is not a size, and changes nothing.</summary>
    public Task SetCanvasSizeAsync(int width, int height) =>
        width < 1 || height < 1 || (Layout.CanvasWidth == width && Layout.CanvasHeight == height)
            ? Task.CompletedTask
            : ChangeAsync(Layout with { CanvasWidth = width, CanvasHeight = height });

    /// <summary>
    /// The view drawn on the canvas: where it starts and its size, in one
    /// change undo takes back. Nothing moves: what stands before the view or
    /// past it is not shown out of design.
    /// </summary>
    public Task SetCanvasAsync(CellBox view) =>
        view.W < 1 || view.H < 1 || view.X < 0 || view.Y < 0
            ? Task.CompletedTask
            : ChangeAsync(Layout with
            {
                CanvasLeft = view.X > 0 ? view.X : null,
                CanvasTop = view.Y > 0 ? view.Y : null,
                CanvasWidth = view.W,
                CanvasHeight = view.H,
            });

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
    /// the group is taken whole. A card takes it as any group does, beside
    /// its own pieces. Refused — false, nothing changed — where it would take
    /// cells another object stands on, or where it lives only in its own card.
    /// </summary>
    public async Task<bool> JoinAsync(ObjectInstance placed)
    {
        if (placed.Group is not { } groupId || Layout.FindGroup(groupId) is null || CardOnly(placed) || !Layout.Fits(placed))
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
        // A card stays a card: what is left of it is its own pieces, never an object set loose.
        if (next.Members(groupId) is { Count: <= 1 } left && !left.Any(CardOnly))
        {
            next = next.Ungrouped(groupId);
        }

        await ChangeAsync(next);
        Select(moved.Id);
        return true;
    }

    /// <summary>
    /// A group moved or resized: its objects go with it by as much as its
    /// corner moved. Made smaller than what it holds, what is left outside
    /// stays where it is, its own still. Refused — false, nothing changed —
    /// only where it would take cells that are not its own, or pass the canvas.
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

        // The cards inside it go with it, each with its objects, by as much.
        var (dx, dy) = (changed.X - current.X, changed.Y - current.Y);
        var members = Moved(changed.Id, dx, dy);
        var cards = MovedCards(changed.Id, dx, dy);
        return Layout.FitsGroup(changed)
            ? ChangeAsync(cards.Aggregate(Layout.With(changed, members), (layout, card) => layout.With(card, Moved(card.Id, dx, dy))))
            : Task.FromResult(false);
    }

    /// <summary>
    /// Cards as they should now look (their colours, elevation, line, anchor),
    /// each standing where it stands: one change, so one undo takes it back.
    /// One that is not on the dashboard, or is as it was, is left alone.
    /// </summary>
    public Task SetLooksAsync(IReadOnlyList<DashboardGroup> cards)
    {
        var next = cards.Where(card => Layout.FindGroup(card.Id) is { } current && current != card)
            .Aggregate(Layout, (layout, card) => layout.With(card, layout.Members(card.Id)));
        return ReferenceEquals(next, Layout) ? Task.CompletedTask : ChangeAsync(next);
    }

    /// <summary>The cards inside a group, however deep, as they stand once it is moved by <paramref name="dx"/>, <paramref name="dy"/>.</summary>
    public IReadOnlyList<DashboardGroup> MovedCards(string groupId, int dx, int dy) =>
        [.. Layout.Descendants(groupId).Select(card => card with { X = card.X + dx, Y = card.Y + dy })];

    /// <summary>
    /// A group made of a card's own pieces, inside that card: no look of its
    /// own — the card's ground showing through it, no line, flat — so the
    /// pieces look as they did, and moved, wrapped and anchored as one.
    /// </summary>
    private static DashboardGroup PiecesGroup(string id, CellBox box, string card) =>
        new(id, box.X, box.Y, box.W, box.H, Background: ThemeColor.Inherited, Elevation: 0, Border: ThemeColor.Transparent, Parent: card);

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
    /// Group (Ctrl+G) with several chosen on one level: a frame round them at
    /// that level — on the page, or inside the card they stand in (<see cref="PiecesGroup"/>) —,
    /// and they stand in it as they are, a card with all it holds (cards all of
    /// one template make it the place more of them go, <see cref="DashboardGroup.Holds"/>); nothing
    /// chosen is taken apart, so Ungroup takes off this one level and no
    /// other. The group is taken whole. Refused — false, nothing changed, and
    /// said (<see cref="Refused"/>) — when the frame would take the cells of
    /// anything not chosen.
    /// </summary>
    public async Task<bool> GroupChosenAsync()
    {
        if (!CanGroup)
        {
            return false;
        }

        var level = LevelOf(_chosen[0]);
        var id = Guid.NewGuid().ToString("N");
        var members = ChosenObjects.Select(o => o with { Group = id }).ToList();
        var cards = ChosenGroups.Select(g => g with { Parent = id }).ToList();
        var frame = CellBox.Around([.. members.Select(o => o.Box), .. cards.Select(g => g.Box)]);
        // Cards all of one template, and nothing else: the group is where more of them go (Holds).
        var holds = members.Count == 0 && cards.Select(g => g.Template).Distinct().ToList() is [{ } template] ? template : null;
        var group = (level is null ? new DashboardGroup(id, frame.X, frame.Y, frame.W, frame.H) : PiecesGroup(id, frame, level)) with { Holds = holds };
        var next = cards.Aggregate(Layout.With(group, members), (layout, inner) => layout.With(inner, []));
        if (!next.FitsGroup(group))
        {
            Refused?.Invoke("They cannot be grouped: the frame round them would cover something that is not chosen. Choose it too, or move it out of the way.");
            return false;
        }

        await ChangeAsync(next);
        SelectGroup(id);
        return true;
    }

    /// <summary>
    /// Ungroup (Ctrl+Shift+G) on the groups chosen: their frames go, and their
    /// objects stand where they are, chosen together — alone on the page, or
    /// back in the card a group of its pieces stood in; a card whose pieces
    /// live only in it stays whole.
    /// </summary>
    public async Task<bool> UngroupAsync()
    {
        var groups = ChosenGroups.Where(Ungroupable).ToList();
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

    /// <summary>The last change taken back, and pressed again the one before it, back to the last hundred; the selection stays where it was if that is still there.</summary>
    public async Task UndoAsync()
    {
        if (_before.Count == 0)
        {
            return;
        }

        Layout = _before[^1];
        _before.RemoveAt(_before.Count - 1);
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
        _before.Add(Layout);
        if (_before.Count > UndoSteps)
        {
            _before.RemoveAt(0);
        }

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
