using Berpiztu.Dashboard.Catalogue;
using Berpiztu.Dashboard.Model;
using Berpiztu.Dashboard.Sources;
using Berpiztu.Dashboard.Storage;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// The board of every object: each kind drawn
/// once as it is born in one view — its default where one was designed, its
/// descriptor's otherwise — so how each is born is designed where it can be
/// seen, the landscape view's and the portrait one's apart. Every card made already is
/// drawn whole, as it is dropped — a container's, an image's, a volume's, a
/// network's, the System card, the charts' — and an object of one is
/// designed in it, never taken out of it; the objects
/// row each. Each reads the first of its family. It is not a dashboard and
/// keeps nothing of its own: each change made to an object — its look, its
/// cells, its place in its card — or to a card's size is written as its
/// kind's default in the view (<see cref="IObjectDefaults"/>).
/// </summary>
public sealed class ObjectDefaultsBoard(
    string view, ObjectCatalogue catalogue, IEnumerable<IDashboardTemplate> templates, SourceFamilies families,
    IObjectDefaults defaults, int columns) : IDashboardStore
{
    /// <summary>The cards whose objects are designed under the card, not their kind: those made of other cards, by their type.</summary>
    private readonly Dictionary<string, HalvedCardsTemplate> _ofCards = templates.OfType<HalvedCardsTemplate>().ToDictionary(card => card.Type);

    /// <summary>Cells left between two objects or cards, and between two rows, so each can be grown without moving the others.</summary>
    private const int Gap = 2;

    /// <summary>Each kind's default as last written, to write only what changed.</summary>
    private readonly Dictionary<string, string> _written = [];

    public async Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await defaults.LoadAsync(cancellationToken);
        var shelf = new Shelf(columns);
        var objects = new List<ObjectInstance>();
        var groups = new List<DashboardGroup>();
        var carded = templates.SelectMany(template => template.Items).Select(item => item.Type).ToHashSet();
        foreach (var kind in catalogue.All.Where(kind => !carded.Contains(kind.Type))
                     .OrderBy(kind => kind.About.Group, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(kind => kind.Label, StringComparer.CurrentCultureIgnoreCase))
        {
            var born = kind.Born(kind.Type, defaults, view, columns);
            var (x, y) = shelf.Place(born.W, born.H, kind.About.Group);
            objects.Add(await ReadingAsync(kind, born with { X = x, Y = y }));
        }

        foreach (var template in templates)
        {
            var card = template.Born(defaults, view);
            var (x, y) = shelf.Place(card.W, card.H, template.Group);
            groups.Add(card.Framed(template.Type, x, y));
            foreach (var o in card.Objects)
            {
                objects.Add(await ReadingAsync(catalogue.Find(o.Type), o with { X = x + o.X, Y = y + o.Y, Group = template.Type }));
            }
        }

        var board = new DashboardLayout(columns, objects) { Groups = groups };
        foreach (var (type, value) in Designed(board))
        {
            _written[type] = value.Write();
        }

        return board.Write();
    }

    /// <summary>Each object or card changed written as its kind's default in this view; one the agent does not keep is said so.</summary>
    public async Task SaveAsync(string text, CancellationToken cancellationToken = default)
    {
        foreach (var (type, value) in Designed(DashboardLayout.Read(text)))
        {
            var written = value.Write();
            if (_written.GetValueOrDefault(type) == written)
            {
                continue;
            }

            if (!await defaults.SetAsync(view, type, value, cancellationToken))
            {
                throw new InvalidOperationException("The agent did not keep it: only a development build writes how objects are born.");
            }

            _written[type] = written;
        }
    }

    /// <summary>
    /// What the board says each kind is born with: every object's look and
    /// cells, and its place in its card; every card's size, under its
    /// template's type (a card's size and where its objects stand are
    /// designed here too).
    /// </summary>
    private IEnumerable<(string Type, ObjectDefault Value)> Designed(DashboardLayout board) =>
        board.Objects.Select(o => (KeyOf(o), ObjectDefault.Of(o, board.FindGroup(o.Group))))
            .Concat(board.Groups.Select(card => (card.Id, ObjectDefault.Of(card))));

    /// <summary>Where an object's design is kept: under its card where the card is made of other cards, under its kind otherwise.</summary>
    private string KeyOf(ObjectInstance o) =>
        o.Group is { } card && _ofCards.TryGetValue(card, out var ofCards) ? ofCards.DefaultOf(o.Type) : o.Type;

    /// <summary>The object reading, on the board, its kind's own default, or the first of its family there is, so it is seen drawn.</summary>
    private async Task<ObjectInstance> ReadingAsync(ObjectDescriptor? kind, ObjectInstance o)
    {
        if (kind is null || !kind.HasSource || kind.SourceOf(o) is not null || families.Find(kind.About.Source) is not { } family)
        {
            return o;
        }

        return o with { Source = (await family.ListAsync()).FirstOrDefault()?.Value };
    }

    /// <summary>
    /// Where the next object or card goes: after the last on its row, a new
    /// row where it would pass the board's edge or where the toolbox's group
    /// changes.
    /// </summary>
    private sealed class Shelf(int columns)
    {
        private int _x;
        private int _y;
        private int _rowHeight;
        private string? _group;

        public (int X, int Y) Place(int w, int h, string group)
        {
            if (_group is not null && (group != _group || _x + w > columns))
            {
                (_x, _y, _rowHeight) = (0, _y + _rowHeight + Gap, 0);
            }

            _group = group;
            var place = (_x, _y);
            _x += w + Gap;
            _rowHeight = Math.Max(_rowHeight, h);
            return place;
        }
    }
}
