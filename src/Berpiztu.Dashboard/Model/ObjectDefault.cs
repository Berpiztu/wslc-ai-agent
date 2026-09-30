using System.Text.Json;
using System.Text.Json.Serialization;

namespace Berpiztu.Dashboard.Model;

/// <summary>
/// The two views a dashboard is designed for, one for a screen wider than it
/// is tall and one for a screen taller than it is wide; the screen's
/// orientation chooses between them, not its size.
/// </summary>
public static class DashboardView
{
    public const string Landscape = "landscape";

    public const string Portrait = "portrait";

    /// <summary>
    /// The columns the portrait view is designed on: a list page's card
    /// across, 24 cells, 402px (a container card has to fit a phone's view;
    /// its 390px held 23, one short). Out of design
    /// the view is fitted to the phone's own width.
    /// </summary>
    public const int PhoneColumns = 24;

    /// <summary>
    /// The height a phone standing up leaves the dashboard, in the page's
    /// pixels: a 390 by 844px screen less the browser's bar and the
    /// application's top and bottom bars, about 9:16 (the whole screen's
    /// 9:19.5 left room at the sides).
    /// </summary>
    public const int PhoneHeight = 670;

    /// <summary>
    /// A view's frame while none is set:
    /// the portrait one what a phone standing up leaves the dashboard, a list
    /// card across, 24 cells by 40; the landscape one a screen of 16:9, 64
    /// cells by 36.
    /// </summary>
    public static (int Columns, int Rows) DefaultCanvas(string view) =>
        view == Portrait ? (PhoneColumns, (int)Math.Floor(PhoneHeight / DashboardLayout.CellPixels)) : (64, 36);
}

/// <summary>
/// How a kind of object is born in one view: its cells, its type size, its alignment and margins, its colours and its
/// parts, designed for each kind on a board of every object and kept with
/// the application. An object dropped from the toolbox takes it, and Reset
/// brings it back; a kind with none is born as its descriptor says. An
/// object of a card made already keeps its place in the card too
/// (<paramref name="X"/>, <paramref name="Y"/>), each kind standing in one
/// card; and a card keeps its own size under its template's type, the cells
/// alone.
/// </summary>
/// <param name="X">Its column in its card, from the card's first; null for one standing alone, or where the card's template puts it.</param>
/// <param name="Y">Its row in its card; null as <paramref name="X"/> is.</param>
public sealed record ObjectDefault(
    int W,
    int H,
    TypeSize Size = TypeSize.Medium,
    HorizontalAlign? Horizontal = null,
    VerticalAlign? Vertical = null,
    ThemeColor? Background = null,
    ThemeColor? Foreground = null,
    IReadOnlyDictionary<string, PartStyle>? Parts = null,
    int? MarginX = null,
    int? MarginY = null,
    int? X = null,
    int? Y = null,
    int? Elevation = null,
    HorizontalAnchor? Anchor = null)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// What an object as it stands would be born with: its look and its cells,
    /// and, in <paramref name="card"/>, its place in it; not where it stands on
    /// the dashboard, nor what it reads.
    /// </summary>
    public static ObjectDefault Of(ObjectInstance o, DashboardGroup? card = null) =>
        new(o.W, o.H, o.Size, o.Horizontal, o.Vertical, o.Background, o.Foreground, o.Parts, o.MarginX, o.MarginY,
            card is null ? null : o.X - card.X, card is null ? null : o.Y - card.Y, o.Elevation, o.Anchor);

    /// <summary>
    /// A card as it is born, kept under its template's type: its cells, and
    /// its colours, its elevation and its anchor (otherwise the Charts card
    /// given another background on the board was dropped on the dashboard
    /// with the page's).
    /// </summary>
    public static ObjectDefault Of(DashboardGroup card) =>
        new(card.W, card.H, Background: card.Background, Foreground: card.Foreground, Elevation: card.Elevation, Anchor: card.Anchor);

    /// <summary>The card with this look: its colours, its elevation and its anchor; its cells are the born card's (<see cref="Catalogue.BornCard"/>).</summary>
    public DashboardGroup On(DashboardGroup card) =>
        card with { Background = Background, Foreground = Foreground, Elevation = Elevation, Anchor = Anchor };

    /// <summary>The object with this look and these cells, where it stands and what it reads kept.</summary>
    public ObjectInstance On(ObjectInstance o) =>
        o with
        {
            W = W,
            H = H,
            Size = Size,
            Horizontal = Horizontal,
            Vertical = Vertical,
            Background = Background,
            Foreground = Foreground,
            Parts = Parts,
            MarginX = MarginX,
            MarginY = MarginY,
            Elevation = Elevation,
            Anchor = Anchor,
        };

    /// <summary>As it is written, which is also how two are compared: its parts are a dictionary, which a record does not compare by what it holds.</summary>
    public string Write() => JsonSerializer.Serialize(this, Json);

    /// <summary>
    /// Every kind's default in every view, as written: views, then kinds; an
    /// empty or unreadable text is none, and a kind that does not read as one
    /// is left out.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, ObjectDefault>> ReadAll(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new Dictionary<string, IReadOnlyDictionary<string, ObjectDefault>>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, ObjectDefault>>>(text, Json) is { } views
                ? views.ToDictionary(view => view.Key, view => (IReadOnlyDictionary<string, ObjectDefault>)view.Value)
                : new Dictionary<string, IReadOnlyDictionary<string, ObjectDefault>>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, IReadOnlyDictionary<string, ObjectDefault>>();
        }
    }
}
