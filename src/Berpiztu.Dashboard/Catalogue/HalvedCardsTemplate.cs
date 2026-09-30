using Berpiztu.Dashboard.Model;
using Berpiztu.Dashboard.Storage;

namespace Berpiztu.Dashboard.Catalogue;

/// <summary>
/// A card made of other cards made already (the four charts in one), <see cref="PerRow"/> to a row, each born at half its
/// cells and a type size smaller, so its words fit the half. It is designed
/// on the board of every object as any card is, but its objects stand in
/// other cards there too, so what is designed of each here is kept under
/// this card (<see cref="DefaultOf"/>), apart from its kind's.
/// </summary>
/// <param name="Above">
/// Objects of its own over the cards, at their own cells and size, not halved
/// (the name of what the charts show, on the card's first line); the cards stand under the lowest of them.
/// </param>
public sealed record HalvedCardsTemplate(string Type, string Label, string Icon, string Group, string Subgroup,
    IReadOnlyList<IDashboardTemplate> Cards, int PerRow, IReadOnlyList<TemplateItem>? Above = null) : IDashboardTemplate
{
    /// <summary>Its objects as they are born with nothing designed.</summary>
    public IReadOnlyList<TemplateItem> Items =>
        [.. Born(null, string.Empty).Objects.Select(o => new TemplateItem(o.Type, o.X, o.Y, o.W, o.H, o.Size, o.Parts, o.Vertical, o.MarginY))];

    /// <summary>Where how one of its objects is born is kept: under this card, not its kind.</summary>
    public string DefaultOf(string type) => $"{Type}.{type}";

    /// <summary>
    /// The objects <see cref="Above"/> the cards, and under them the cards
    /// as each is born in the view, halved, laid side by side — a
    /// row as tall as its tallest card, a column as wide as its widest — and
    /// then each object as it was designed in this card, where it was; each
    /// object's id its <see cref="DefaultOf"/>, so on the board it is not
    /// taken for the same kind standing in its own card.
    /// </summary>
    public BornCard Born(IObjectDefaults? defaults, string view)
    {
        var halves = Cards.Select(card => Halved(card.Born(defaults, view))).ToList();
        var columnWidth = halves.Max(half => half.W);
        List<ObjectInstance> objects = [.. (Above ?? []).Select(item => new ObjectInstance(item.Type, item.Type, item.X, item.Y, item.W, item.H,
            Size: item.Size, Vertical: item.Vertical, Parts: item.Parts, MarginY: item.MarginY))];
        var top = objects.Select(o => o.Y + o.H).DefaultIfEmpty(0).Max();
        foreach (var row in halves.Chunk(PerRow))
        {
            objects.AddRange(row.SelectMany((half, column) => half.Objects.Select(o => o with { X = column * columnWidth + o.X, Y = top + o.Y })));
            top += row.Max(half => half.H);
        }

        var designed = objects.Select(o => defaults?.For(view, DefaultOf(o.Type)) is { } own
            ? own.On(o) with { Id = DefaultOf(o.Type), X = own.X ?? o.X, Y = own.Y ?? o.Y }
            : o with { Id = DefaultOf(o.Type) }).ToList();
        var reach = CellBox.Around(designed.Select(o => o.Box));
        var frame = defaults?.For(view, Type);
        return new BornCard(Type, designed,
            Math.Max(frame?.W ?? columnWidth * Math.Min(PerRow, halves.Count), reach.X + reach.W),
            Math.Max(frame?.H ?? top, reach.Y + reach.H), frame);
    }

    private static BornCard Halved(BornCard card) => card with
    {
        Objects = [.. card.Objects.Select(o => o with
        {
            X = o.X / 2,
            Y = o.Y / 2,
            W = Math.Max(1, o.W / 2),
            H = Math.Max(1, o.H / 2),
            Size = (TypeSize)Math.Max((int)TypeSize.Small, (int)o.Size - 1),
            MarginX = o.MarginX / 2,
            MarginY = o.MarginY / 2,
        })],
        W = Math.Max(1, card.W / 2),
        H = Math.Max(1, card.H / 2),
    };
}
