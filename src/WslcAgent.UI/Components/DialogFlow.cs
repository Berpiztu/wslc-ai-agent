using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace WslcAgent.UI.Components;

/// <summary>
/// What a confirmation was answered with: the button, and whether the box
/// saying to answer the rest the same way was ticked. The box speaks for
/// what is still to be asked and for nothing already answered, so five files
/// refused one by one stay refused when the sixth is accepted for all.
/// </summary>
public sealed record Confirmed(bool Yes, bool ForAll, bool Other = false);

/// <summary>What is done with work not saved that is being left: stay with it, let it go, or save it and go.</summary>
public enum UnsavedChoice
{
    Stay,
    Discard,
    Save,
}

/// <summary>The ways the UI asks the user something: a form dialog, a picker that returns a name, and a yes/cancel confirmation.</summary>
public static class DialogFlow
{
    /// <summary>No cross in the corner anywhere: a dialog carries Close (or Cancel and its verb) in its title row.</summary>
    private static readonly DialogOptions Small = new()
    {
        CloseButton = false,
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        BackdropClick = false,
    };

    /// <summary>
    /// A confirmation opens with Cancel focused — its first button — so Enter,
    /// pressed by habit, answers no: nothing is removed, discarded or saved
    /// without choosing the button that does it.
    /// </summary>
    private static readonly DialogOptions Confirmation = Small with { DefaultFocus = DefaultFocus.FirstChild };

    /// <summary>For the launch form and the host folder picker: wider, tall enough for a form.</summary>
    private static readonly DialogOptions Large = Small with { MaxWidth = MaxWidth.Medium };

    /// <summary>Shows a form dialog; true when it closed by submitting.</summary>
    public static async Task<bool> ShowAsync<TDialog>(IDialogService dialogs, string title, DialogParameters? parameters = null, bool large = false)
        where TDialog : IComponent
    {
        var reference = await dialogs.ShowAsync<TDialog>(title, parameters ?? new DialogParameters(), large ? Large : Small);
        var result = await reference.Result;
        return result is { Canceled: false };
    }

    /// <summary>A dialog the user can only leave through its own buttons (the backup job, an image build): no close button, no backdrop click, no Escape. True when it closed by submitting.</summary>
    public static async Task<bool> ShowPersistentAsync<TDialog>(IDialogService dialogs, string title, DialogParameters? parameters = null)
        where TDialog : IComponent
    {
        var reference = await dialogs.ShowAsync<TDialog>(title, parameters ?? new DialogParameters(), Small with { CloseOnEscapeKey = false });
        var result = await reference.Result;
        return result is { Canceled: false };
    }

    /// <summary>
    /// A window over the whole screen. Without a title row when the window draws its own
    /// bar (the host browser pane, whose bar says where it is); with one, Close in it, when
    /// it only shows something (the architecture diagram). Escape does not close it: the key
    /// belongs to what the window shows.
    /// </summary>
    public static async Task ShowFullScreenAsync<TDialog>(IDialogService dialogs, string title, DialogParameters parameters, bool titled = false)
        where TDialog : IComponent
    {
        var options = new DialogOptions { FullScreen = true, NoHeader = !titled, CloseButton = false, BackdropClick = false, CloseOnEscapeKey = false };
        var reference = await dialogs.ShowAsync<TDialog>(title, parameters, options);
        await reference.Result;
    }

    /// <summary>The same, for a dialog type known only at run time (a picker's create dialog).</summary>
    public static async Task<bool> ShowAsync(IDialogService dialogs, Type dialog, string title)
    {
        var reference = await dialogs.ShowAsync(dialog, title, new DialogParameters(), Small);
        var result = await reference.Result;
        return result is { Canceled: false };
    }

    /// <summary>Shows a picker; the chosen name, or null when closed.</summary>
    public static async Task<string?> PickAsync<TDialog>(IDialogService dialogs, string title, DialogParameters? parameters = null)
        where TDialog : IComponent
    {
        var reference = await dialogs.ShowAsync<TDialog>(title, parameters ?? new DialogParameters(), Small);
        var result = await reference.Result;
        return result is { Canceled: false, Data: string name } && name.Length > 0 ? name : null;
    }

    /// <summary>A picker that returns an item rather than a name (a browser session); null when closed.</summary>
    public static async Task<TItem?> ChooseAsync<TDialog, TItem>(IDialogService dialogs, string title, DialogParameters parameters)
        where TDialog : IComponent
        where TItem : class
    {
        var reference = await dialogs.ShowAsync<TDialog>(title, parameters, Small);
        var result = await reference.Result;
        return result is { Canceled: false, Data: TItem item } ? item : null;
    }

    /// <summary>The list editor behind a multi-value field; the edited values, or null when closed.</summary>
    public static async Task<IReadOnlyList<string>?> EditValuesAsync(
        IDialogService dialogs, string title, string label, IReadOnlyList<string> values, string placeholder, string hint)
    {
        var parameters = new DialogParameters<Dialogs.ValueListDialog>
        {
            { d => d.Values, values },
            { d => d.Label, label },
            { d => d.Placeholder, placeholder },
            { d => d.Hint, hint },
        };
        var reference = await dialogs.ShowAsync<Dialogs.ValueListDialog>(title, parameters, Small);
        var result = await reference.Result;
        return result is { Canceled: false, Data: IReadOnlyList<string> edited } ? edited : null;
    }

    /// <summary>
    /// One text field and a verb (a folder name, a new name); the trimmed
    /// text, or null when cancelled. <paramref name="value"/> is what the
    /// field starts with — a name being changed opens already selected, so
    /// only what has to change is typed.
    /// </summary>
    public static async Task<string?> PromptAsync(IDialogService dialogs, string title, string label, string helperText, string submitText, string value = "")
    {
        var parameters = new DialogParameters<Dialogs.PromptDialog>
        {
            { d => d.Label, label },
            { d => d.HelperText, helperText },
            { d => d.SubmitText, submitText },
            { d => d.Value, value },
        };
        var reference = await dialogs.ShowAsync<Dialogs.PromptDialog>(title, parameters, Small);
        var result = await reference.Result;
        return result is { Canceled: false, Data: string text } && text.Length > 0 ? text : null;
    }

    /// <summary>An error and its explanation, with Close alone: shown before a verb runs when the form already knows it would be refused.</summary>
    public static async Task ExplainAsync(IDialogService dialogs, string title, string message, string explanation)
    {
        var parameters = new DialogParameters<Dialogs.MessageDialog>
        {
            { d => d.Message, message },
            { d => d.Explanation, explanation },
        };
        var reference = await dialogs.ShowAsync<Dialogs.MessageDialog>(title, parameters, Small);
        await reference.Result;
    }

    /// <summary>Yes/cancel; the yes text is the verb, in the error colour when <paramref name="destructive"/>.</summary>
    public static async Task<bool> ConfirmAsync(IDialogService dialogs, string title, string message, string yes, bool destructive = false) =>
        (await AskAsync(dialogs, title, message, yes, destructive)).Yes;

    /// <summary>
    /// The same confirmation, with a box that answers the rest the same way,
    /// for a question that is one of several — a file that is already there,
    /// and the next one, and the next. Whichever button is pressed with the
    /// box ticked is the answer to all of them; left unticked, each is asked
    /// for itself.
    /// </summary>
    public static Task<Confirmed> AskEachAsync(IDialogService dialogs, string title, string message, string yes, string forAll, bool destructive = false) =>
        AskAsync(dialogs, title, message, yes, destructive, forAll);

    private static async Task<Confirmed> AskAsync(IDialogService dialogs, string title, string message, string yes, bool destructive, string forAll = "")
    {
        var parameters = new DialogParameters<Dialogs.ConfirmDialog>
        {
            { d => d.Message, message },
            { d => d.YesText, yes },
            { d => d.Destructive, destructive },
            { d => d.ForAll, forAll },
        };
        var reference = await dialogs.ShowAsync<Dialogs.ConfirmDialog>(title, parameters, Confirmation);
        var result = await reference.Result;
        // Dismissed — Escape, the overlay — is a no, and a no that says
        // nothing about the rest: the user answered nothing, so nothing is
        // answered for them.
        return result is { Canceled: false, Data: Confirmed answered } ? answered : new Confirmed(false, false);
    }

    /// <summary>
    /// Work not saved is being left (Done, Back, a link): Cancel stays with it,
    /// Discard lets it go, Save keeps it and goes — the quickest way out when
    /// the changes are wanted. Every screen with unsaved work asks this.
    /// </summary>
    public static async Task<UnsavedChoice> LeaveUnsavedAsync(IDialogService dialogs, string message)
    {
        var parameters = new DialogParameters<Dialogs.ConfirmDialog>
        {
            { d => d.Message, message },
            { d => d.YesText, "Discard" },
            { d => d.Destructive, true },
            { d => d.OtherText, "Save" },
        };
        var reference = await dialogs.ShowAsync<Dialogs.ConfirmDialog>("Unsaved changes", parameters, Confirmation);
        var result = await reference.Result;
        // Dismissed — Escape, the overlay — stays: nothing was answered, so nothing is thrown away.
        return result is { Canceled: false, Data: Confirmed answered }
            ? answered.Other ? UnsavedChoice.Save : answered.Yes ? UnsavedChoice.Discard : UnsavedChoice.Stay
            : UnsavedChoice.Stay;
    }

    /// <summary>The removal confirmation every resource uses.</summary>
    public static Task<bool> ConfirmRemoveAsync(IDialogService dialogs, string kind, string what) =>
        ConfirmAsync(dialogs, $"Remove {kind}", $"Remove {what}? This cannot be undone.", "Remove", destructive: true);
}
