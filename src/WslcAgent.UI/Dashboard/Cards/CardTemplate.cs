using Berpiztu.Dashboard.Catalogue;

namespace WslcAgent.UI.Dashboard.Cards;

/// <summary>A card made already: its objects and their places, as the toolbox offers it under its subgroup.</summary>
/// <param name="Inner">The groups it is born with (a project card's place for its containers); none by default.</param>
public sealed record CardTemplate(string Type, string Label, string Icon, string Subgroup, IReadOnlyList<TemplateItem> Items,
    IReadOnlyList<BornGroup>? Inner = null) : IDashboardTemplate
{
    /// <summary>The toolbox's group the cards made already stand in.</summary>
    public const string CardsGroup = "Cards";

    public string Group => CardsGroup;
}
