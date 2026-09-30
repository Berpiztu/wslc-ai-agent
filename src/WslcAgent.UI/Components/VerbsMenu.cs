namespace WslcAgent.UI.Components;

/// <summary>
/// The list the centre button opens, as its verbs see it: a verb that ran
/// closes it. Cascaded by <c>PageVerbsFab</c> to every <c>VerbFab</c> in the
/// page's <c>Verbs</c>, which are plain buttons, so the list does not close
/// on them by itself.
/// </summary>
public sealed record VerbsMenu(Action Close);
