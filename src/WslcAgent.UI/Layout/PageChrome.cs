using Microsoft.AspNetCore.Components;

namespace WslcAgent.UI.Layout;

/// <summary>
/// What the current page contributes to the layout's top bar: the
/// title, a status slot next to it, a search slot
/// centred in the bar, a navigation slot on the right (e.g. bulk actions), a
/// second row of section actions and the page's verbs, drawn in the navigation
/// button's list. <see cref="PageShell"/> fills it; <see cref="MainLayout"/> renders it.
/// </summary>
public sealed class PageChrome
{
    public string Title { get; private set; } = "";

    /// <summary>What the title bar draws in place of <see cref="Title"/>'s text, when the title holds more than words (an icon); the text stays the tab's name.</summary>
    public RenderFragment? TitleContent { get; private set; }

    public RenderFragment? Status { get; private set; }

    public RenderFragment? TopbarSearch { get; private set; }

    public RenderFragment? TopbarNav { get; private set; }

    public RenderFragment? SectionActions { get; private set; }

    /// <summary>The page's own verbs (Run, Create, Pull…), which the navigation button draws in its list.</summary>
    public RenderFragment? Verbs { get; private set; }

    /// <summary>The page only shows what lives in the WSLC session: with it stopped, the layout disables its verbs.</summary>
    public bool RequiresSession { get; private set; }

    public event Action? Changed;

    public void Set(string title, RenderFragment? status, RenderFragment? topbarSearch, RenderFragment? topbarNav, RenderFragment? sectionActions, RenderFragment? verbs = null, bool requiresSession = false, RenderFragment? titleContent = null)
    {
        Title = title;
        TitleContent = titleContent;
        Status = status;
        TopbarSearch = topbarSearch;
        TopbarNav = topbarNav;
        SectionActions = sectionActions;
        Verbs = verbs;
        RequiresSession = requiresSession;
        Changed?.Invoke();
    }

    public void Clear() => Set("", null, null, null, null);
}
