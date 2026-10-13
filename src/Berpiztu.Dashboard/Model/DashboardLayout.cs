using System.Text.Json;
using System.Text.Json.Serialization;

namespace Berpiztu.Dashboard.Model;

/// <summary>
/// A whole dashboard: how many columns its canvas has, every object on it and
/// the groups that keep some of them together. What the application keeps
/// (<see cref="Storage.IDashboardStore"/>) is this, written by <see cref="Write"/>.
/// </summary>
/// <param name="Columns">The canvas's columns; its cells are square, so the rows follow from the width.</param>
/// <param name="Objects">Every object, in the order they were dropped.</param>
public sealed record DashboardLayout(int Columns, IReadOnlyList<ObjectInstance> Objects)
{
    /// <summary>
    /// The columns a new dashboard has: cells of about 17px on the screen it is
    /// first designed on, so a card is laid out as close to the list pages'
    /// card as cells allow.
    /// </summary>
    public const int DefaultColumns = 48;

    /// <summary>The form this build writes; a later one reads this one and says what changed.</summary>
    private const int FormVersion = 1;

    public static DashboardLayout Empty { get; } = new(DefaultColumns, []);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>The groups, each a frame its objects stand in.</summary>
    public IReadOnlyList<DashboardGroup> Groups { get; init; } = [];

    /// <summary>
    /// A cell's side, the same on every screen and every dashboard (a
    /// container card dropped is as large as the Containers screen's): the
    /// list card's measured on that screen, its
    /// header 50px in three cells, its body 118px in seven, its actions 33px
    /// in two and its 419px across in twenty-four — 16.75px at a 16px rem,
    /// in rem so the page's type and the cells scale together. A wider window
    /// is more cells, not larger ones; the zoom that fits a view to the screen
    /// is its own.
    /// </summary>
    public const string CellSize = "1.046875rem";

    /// <summary>A cell's side in the page's pixels at a 16px rem, for what is counted in cells before anything is measured: a phone's columns.</summary>
    public const double CellPixels = 16.75;

    /// <summary>
    /// The alarms the status bar shows as well, in the order they were ticked; one whose object left,
    /// or whose alarm is off, is not shown.
    /// </summary>
    public IReadOnlyList<StatusAlarm> Status { get; init; } = [];

    /// <summary>
    /// The view's size in cells, set in design: what is shown out of design, from the canvas's first cell,
    /// rooms and all, zoomed to fit the screen; its aspect is the view's. Null
    /// until it is set, and the view is then what its cards take.
    /// </summary>
    public int? CanvasWidth { get; init; }

    /// <summary>The view's height in cells, set with <see cref="CanvasWidth"/>.</summary>
    public int? CanvasHeight { get; init; }

    /// <summary>
    /// The view's first column on the canvas, set in design by drawing it
    /// where it is wanted: what is shown out of design starts there, and what
    /// stands before it is not shown. Null is the canvas's first column.
    /// </summary>
    public int? CanvasLeft { get; init; }

    /// <summary>The view's first row on the canvas, set with <see cref="CanvasLeft"/>.</summary>
    public int? CanvasTop { get; init; }

    /// <summary>Where the view starts on the canvas, its first column and row: the canvas's first cell until it is drawn elsewhere.</summary>
    public (int Column, int Row) CanvasOrigin => (CanvasLeft ?? 0, CanvasTop ?? 0);

    /// <summary>The object with this id, if the dashboard has it.</summary>
    public ObjectInstance? Find(string id) => Objects.FirstOrDefault(o => o.Id == id);

    /// <summary>The group with this id, if the dashboard has it.</summary>
    public DashboardGroup? FindGroup(string? id) => id is null ? null : Groups.FirstOrDefault(g => g.Id == id);

    /// <summary>The objects standing in the group of this id.</summary>
    public IReadOnlyList<ObjectInstance> Members(string groupId) => [.. Objects.Where(o => o.Group == groupId)];

    /// <summary>
    /// Whether <paramref name="candidate"/> can stand where it says: inside the
    /// canvas's columns, on cells no other object takes, and — in a group —
    /// inside that group's frame, or — standing alone — outside every frame.
    /// The object it replaces (the same id) does not count against it, and a
    /// hidden one takes no cells (what a hidden piece leaves is free for the
    /// others), and it is shown again only
    /// where nothing stands.
    /// </summary>
    public bool Fits(ObjectInstance candidate)
    {
        var box = candidate.Box;
        if (!OnCanvas(box) || (candidate.Hidden != true && Objects.Any(o => o.Id != candidate.Id && o.Hidden != true && o.Box.Overlaps(box))))
        {
            return false;
        }

        return candidate.Group is { } groupId
            ? FindGroup(groupId) is { } group && group.Box.Contains(box) && (candidate.Hidden == true || Cards(groupId).All(card => !card.Box.Overlaps(box)))
            : Groups.All(group => !group.Box.Overlaps(box));
    }

    /// <summary>The cards that stand inside this one (<see cref="DashboardGroup.Parent"/>): a project's containers, each a card of its own in the project's.</summary>
    public IReadOnlyList<DashboardGroup> Cards(string groupId) => [.. Groups.Where(card => card.Parent == groupId)];

    /// <summary>
    /// Whether <paramref name="candidate"/> — a group moved or resized — can
    /// stand where it says with <paramref name="members"/>, its objects as they
    /// would then stand: on the canvas, holding all of them, and on no cell of
    /// another group or of an object that is not its own. A card that holds
    /// others holds them still, where they would then stand
    /// (<paramref name="cards"/>; where they stand now when none are given),
    /// and what is theirs is its own; a card inside another stays inside it,
    /// and off what else that card holds.
    /// </summary>
    public bool FitsGroup(DashboardGroup candidate, IReadOnlyList<ObjectInstance> members, IReadOnlyList<DashboardGroup>? cards = null)
    {
        var box = candidate.Box;
        return OnCanvas(box)
            && members.All(member => box.Contains(member.Box))
            && (cards ?? Cards(candidate.Id)).All(card => box.Contains(card.Box))
            && (candidate.Parent is not { } parentId || (FindGroup(parentId) is { } parent && parent.Box.Contains(box)))
            && Groups.All(group => group.Id == candidate.Id || group.Id == candidate.Parent || group.Parent == candidate.Id || !group.Box.Overlaps(box))
            // A hidden piece takes no cells: what it left is free for a card as it is for an object.
            && Objects.All(o => o.Group == candidate.Id || o.Hidden == true || FindGroup(o.Group)?.Parent == candidate.Id || !o.Box.Overlaps(box));
    }

    /// <summary>
    /// Where <paramref name="dropped"/> goes when it is let go on
    /// <paramref name="target"/> to make a group of the two: the one that was there stays, and the one dropped
    /// stands at its right, or at its left when there is no room there, or
    /// under it; null when neither place has room for it and the frame round
    /// the two.
    /// </summary>
    public ObjectInstance? Beside(ObjectInstance target, ObjectInstance dropped)
    {
        var loose = dropped with { Group = null };
        ObjectInstance[] places =
        [
            loose with { X = target.X + target.W, Y = target.Y },
            loose with { X = target.X - loose.W, Y = target.Y },
            loose with { X = target.X, Y = target.Y + target.H },
        ];
        return places.FirstOrDefault(place =>
        {
            var frame = CellBox.Around([target.Box, place.Box]);
            return OnCanvas(place.Box)
                && Groups.All(group => !group.Box.Overlaps(frame))
                && Objects.All(o => o.Id == target.Id || o.Id == dropped.Id || !o.Box.Overlaps(frame));
        });
    }

    /// <summary>
    /// The group whose frame holds this box whole, if one does: an object
    /// carried there, or dropped there from the toolbox, would stand in that
    /// group, where the user put it.
    /// </summary>
    public DashboardGroup? GroupHolding(CellBox box) =>
        // The card inside before the one that holds it: both hold the box, and the inner one is where it was put.
        Groups.Where(group => group.Box.Contains(box)).OrderBy(group => group.Parent is null).FirstOrDefault();

    /// <summary>The dashboard with <paramref name="changed"/> in place of the object of its id, or added when it has none.</summary>
    public DashboardLayout With(ObjectInstance changed) =>
        this with
        {
            Objects = Objects.Any(o => o.Id == changed.Id)
                ? [.. Objects.Select(o => o.Id == changed.Id ? changed : o)]
                : [.. Objects, changed],
        };

    /// <summary>
    /// The dashboard with the objects and groups these ids name moved together
    /// by <paramref name="dx"/>, <paramref name="dy"/>: a group's objects go
    /// with it. What several chosen become when one drag carries them.
    /// </summary>
    public DashboardLayout MovedTogether(IReadOnlyCollection<string> ids, int dx, int dy) =>
        this with
        {
            // A card goes with what stands in it: its objects, and the cards inside it with theirs.
            Objects = [.. Objects.Select(o => ids.Contains(o.Id) || (o.Group is { } g && (ids.Contains(g) || (FindGroup(g)?.Parent is { } holder && ids.Contains(holder)))) ? o.MovedBy(dx, dy) : o)],
            Groups = [.. Groups.Select(g => ids.Contains(g.Id) || (g.Parent is { } holder && ids.Contains(holder)) ? g with { X = g.X + dx, Y = g.Y + dy } : g)],
        };

    /// <summary>Whether every object and group these ids name stands where it may, in this dashboard.</summary>
    public bool FitsAll(IReadOnlyCollection<string> ids) =>
        ids.All(id => Find(id) is { } one ? Fits(one) : FindGroup(id) is not { } group || FitsGroup(group, Members(group.Id)));

    /// <summary>
    /// The dashboard with a card put inside another (<see cref="DashboardGroup.Parent"/>):
    /// a card of its own, with its frame, its line and its elevation, taken
    /// and moved whole, in that card's frame. It goes to the first of the
    /// frame's places that nothing in it stands on, the places being as wide
    /// and as tall as the card, <paramref name="perRow"/> to a row, from the
    /// frame's first cell; and the frame grows to hold it. Null when the
    /// group is not there, or its frame cannot grow that far: it would pass
    /// the dashboard's edge or take cells that are not its own.
    /// </summary>
    /// <param name="card">The card's frame, its size and its id; where it stands is found here.</param>
    /// <param name="objects">The card's objects, each where it stands from the card's top-left cell.</param>
    public DashboardLayout? WithInside(string groupId, DashboardGroup card, IReadOnlyList<ObjectInstance> objects, int perRow)
    {
        if (FindGroup(groupId) is not { } frame)
        {
            return null;
        }

        var members = Members(groupId);
        var cards = Cards(groupId);
        // An object or a card stands on a few places at most: past this many, one of them is free.
        for (var place = 0; place <= ((members.Count + cards.Count) * 4) + perRow; place++)
        {
            var box = new CellBox(frame.X + (place % perRow * card.W), frame.Y + (place / perRow * card.H), card.W, card.H);
            if (members.Any(member => member.Hidden != true && member.Box.Overlaps(box)) || cards.Any(other => other.Box.Overlaps(box)))
            {
                continue;
            }

            var around = CellBox.Around([frame.Box, box]);
            var grown = frame with { W = around.W, H = around.H };
            var placed = card with { X = box.X, Y = box.Y, Parent = groupId };
            return FitsGroup(grown, members, [.. cards, placed])
                ? With(grown, members).With(placed, [.. objects.Select(o => o with { X = box.X + o.X, Y = box.Y + o.Y, Group = placed.Id })])
                : null;
        }

        return null;
    }

    /// <summary>
    /// The dashboard without these cards of a card, each with every object
    /// in it, and that card's frame drawn back to what is left in it from
    /// its first cell: what <see cref="WithInside"/> grew it for is given back.
    /// </summary>
    public DashboardLayout WithoutInside(string groupId, IReadOnlyCollection<string> cardIds)
    {
        var left = cardIds.Aggregate(this, (layout, id) => layout.FindGroup(id)?.Parent == groupId ? layout.WithoutGroup(id) : layout);
        if (left.FindGroup(groupId) is not { } frame)
        {
            return left;
        }

        var members = left.Members(groupId);
        var around = CellBox.Around([.. members.Select(member => member.Box), .. left.Cards(groupId).Select(card => card.Box), new CellBox(frame.X, frame.Y, 1, 1)]);
        return left.With(frame with { W = around.W, H = around.H }, members);
    }

    /// <summary>The dashboard with <paramref name="group"/> in place of the group of its id, or added, and its objects as <paramref name="members"/> say.</summary>
    public DashboardLayout With(DashboardGroup group, IReadOnlyList<ObjectInstance> members)
    {
        IReadOnlyList<DashboardGroup> groups = Groups.Any(g => g.Id == group.Id)
            ? [.. Groups.Select(g => g.Id == group.Id ? group : g)]
            : [.. Groups, group];
        var byId = members.ToDictionary(member => member.Id);
        return this with
        {
            Groups = groups,
            Objects = [.. Objects.Select(o => byId.GetValueOrDefault(o.Id) ?? o), .. members.Where(m => Find(m.Id) is null)],
        };
    }

    /// <summary>The dashboard without this object; a group it leaves with no object goes with it.</summary>
    public DashboardLayout Without(string id)
    {
        var left = this with { Objects = [.. Objects.Where(o => o.Id != id)] };
        return left with { Groups = [.. left.Groups.Where(g => left.Objects.Any(o => o.Group == g.Id))] };
    }

    /// <summary>The dashboard without this group, its objects left standing where they are, alone, and the cards inside it cards on the page.</summary>
    public DashboardLayout Ungrouped(string groupId) =>
        this with
        {
            Groups = [.. Groups.Where(g => g.Id != groupId).Select(g => g.Parent == groupId ? g with { Parent = null } : g)],
            Objects = [.. Objects.Select(o => o.Group == groupId ? o with { Group = null } : o)],
        };

    /// <summary>The dashboard without this group and every object in it, the cards inside it and theirs gone with it.</summary>
    public DashboardLayout WithoutGroup(string groupId)
    {
        var gone = Groups.Where(g => g.Id == groupId || g.Parent == groupId).Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        return this with
        {
            Groups = [.. Groups.Where(g => !gone.Contains(g.Id))],
            Objects = [.. Objects.Where(o => o.Group is null || !gone.Contains(o.Group))],
        };
    }

    public string Write() => JsonSerializer.Serialize(new Stored(FormVersion, Columns, Objects, Groups, Status.Count > 0 ? Status : null, CanvasWidth, CanvasHeight, CanvasLeft, CanvasTop), Json);

    /// <summary>
    /// A stored dashboard read back; an empty or unreadable one is an empty
    /// dashboard, so a damaged file never stops the page. Objects of a type
    /// this build does not know are kept as they are: the page does not draw
    /// them, and a later build still has them.
    /// </summary>
    public static DashboardLayout Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<Stored>(text, Json) is { } stored
                ? new DashboardLayout(stored.Columns > 0 ? stored.Columns : DefaultColumns, stored.Objects ?? [])
                {
                    Groups = stored.Groups ?? [],
                    Status = stored.Status ?? [],
                    CanvasWidth = stored.CanvasWidth is > 0 ? stored.CanvasWidth : null,
                    CanvasHeight = stored.CanvasHeight is > 0 ? stored.CanvasHeight : null,
                    CanvasLeft = stored.CanvasLeft is > 0 ? stored.CanvasLeft : null,
                    CanvasTop = stored.CanvasTop is > 0 ? stored.CanvasTop : null,
                }
                : Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    private bool OnCanvas(CellBox box) => box is { X: >= 0, Y: >= 0, W: >= 1, H: >= 1 } && box.X + box.W <= Columns;

    /// <summary>The written form: the layout and the version of the form it was written in.</summary>
    private sealed record Stored(int Version, int Columns, IReadOnlyList<ObjectInstance>? Objects, IReadOnlyList<DashboardGroup>? Groups,
        IReadOnlyList<StatusAlarm>? Status = null, int? CanvasWidth = null, int? CanvasHeight = null, int? CanvasLeft = null, int? CanvasTop = null);
}
