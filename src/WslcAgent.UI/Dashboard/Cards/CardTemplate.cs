using Berpiztu.Dashboard.Catalogue;

namespace WslcAgent.UI.Dashboard.Cards;

/// <summary>A card made already: its objects and their places, as the toolbox offers it under its subgroup.</summary>
public sealed record CardTemplate(string Type, string Label, string Icon, string Subgroup, IReadOnlyList<TemplateItem> Items) : IDashboardTemplate
{
    /// <summary>The toolbox's group the cards made already stand in.</summary>
    public const string CardsGroup = "Cards";

    public string Group => CardsGroup;
}
