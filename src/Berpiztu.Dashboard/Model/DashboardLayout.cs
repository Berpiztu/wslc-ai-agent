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
            ? FindGroup(groupId) is { } group && group.Box.Contains(box)
            : Groups.All(group => !group.Box.Overlaps(box));
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> — a group moved or resized — can
    /// stand where it says with <paramref name="members"/>, its objects as they
    /// would then stand: on the canvas, holding all of them, and on no cell of
    /// another group or of an object that is not its own.
    /// </summary>
    public bool FitsGroup(DashboardGroup candidate, IReadOnlyList<ObjectInstance> members)
    {
        var box = candidate.Box;
        return OnCanvas(box)
            && members.All(member => box.Contains(member.Box))
            && Groups.All(group => group.Id == candidate.Id || !group.Box.Overlaps(box))
            && Objects.All(o => o.Group == candidate.Id || !o.Box.Overlaps(box));
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
        Groups.FirstOrDefault(group => group.Box.Contains(box));

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
            Objects = [.. Objects.Select(o => ids.Contains(o.Id) || (o.Group is { } g && ids.Contains(g)) ? o.MovedBy(dx, dy) : o)],
            Groups = [.. Groups.Select(g => ids.Contains(g.Id) ? g with { X = g.X + dx, Y = g.Y + dy } : g)],
        };

    /// <summary>Whether every object and group these ids name stands where it may, in this dashboard.</summary>
    public bool FitsAll(IReadOnlyCollection<string> ids) =>
        ids.All(id => Find(id) is { } one ? Fits(one) : FindGroup(id) is not { } group || FitsGroup(group, Members(group.Id)));

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

    /// <summary>The dashboard without this group, its objects left standing where they are, alone.</summary>
    public DashboardLayout Ungrouped(string groupId) =>
        this with
        {
            Groups = [.. Groups.Where(g => g.Id != groupId)],
            Objects = [.. Objects.Select(o => o.Group == groupId ? o with { Group = null } : o)],
        };

    /// <summary>The dashboard without this group and every object in it.</summary>
    public DashboardLayout WithoutGroup(string groupId) =>
        this with
        {
            Groups = [.. Groups.Where(g => g.Id != groupId)],
            Objects = [.. Objects.Where(o => o.Group != groupId)],
        };

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
