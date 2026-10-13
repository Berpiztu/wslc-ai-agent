using Berpiztu.Dashboard.Fields;

namespace Berpiztu.Dashboard.Catalogue;

/// <summary>
/// A card made already that holds, beside its own pieces, the cards of what
/// it reads is made of (a project's card, and in it its containers'), given
/// by the application. It is one card: each of them is a card of its own
/// inside it (<see cref="Model.DashboardGroup.Parent"/>), with its frame, its
/// line and its elevation, taken and moved whole within the card's frame,
/// <see cref="PerRow"/> to a row from the card's own pieces on, and the
/// frame grows to hold them.
/// <para>
/// When what the card reads is first chosen, every one of them comes into
/// it, so the card shows what it holds. Its properties list them, each with
/// a tick: ticked while its card is in the card. One that appears later
/// is listed unticked, for the user to say, and the dashboard stays as it
/// was designed until they do; one that is gone keeps its card, with
/// nothing to read, as any object whose source is gone. One of them wanted
/// on its own is its own card, dropped from the toolbox as ever.
/// </para>
/// </summary>
public interface ICardCompanions
{
    /// <summary>The template whose cards hold others' pieces (<c>wslc.project-card</c>).</summary>
    string Template { get; }

    /// <summary>What the list is called in the properties window ("Containers").</summary>
    string Label { get; }

    /// <summary>What one of them is called, for the words around it ("container").</summary>
    string Noun { get; }

    /// <summary>The card each of them is, inside the card.</summary>
    IDashboardTemplate Card { get; }

    /// <summary>How many of them, the card's own pieces counting as one, stand side by side before the next row begins.</summary>
    int PerRow { get; }

    /// <summary>
    /// The classes the card's frame takes out of design, which say how what
    /// it reads is as a whole (a project's line: every container running,
    /// none, some, something being done to it); empty for none, and the
    /// frame keeps its own line. The application's stylesheet draws them.
    /// </summary>
    string LineOf(string source);

    /// <summary>What <see cref="LineOf"/> answers may be another now: the designer draws its cards again.</summary>
    event Action? Changed;

    /// <summary>
    /// What <paramref name="source"/>, the card's own, holds now: the value
    /// the card of each reads, and the name it is listed by. Read when the
    /// card is selected; empty when there are none, or they could not be read.
    /// </summary>
    Task<IReadOnlyList<FieldOption<string>>> ListAsync(string source);
}

/// <summary>What the application registered, by the template it is of.</summary>
public sealed class CardCompanions(IEnumerable<ICardCompanions> all)
{
    private readonly Dictionary<string, ICardCompanions> _byTemplate = all.ToDictionary(one => one.Template, StringComparer.Ordinal);

    /// <summary>Every one registered, for who follows them all.</summary>
    public IReadOnlyCollection<ICardCompanions> All => _byTemplate.Values;

    public ICardCompanions? Find(string? template) => template is null ? null : _byTemplate.GetValueOrDefault(template);
}
