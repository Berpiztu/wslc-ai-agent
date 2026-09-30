using System.Globalization;
using System.Text.Json;
using Microsoft.JSInterop;
using MudBlazor;

namespace WslcAgent.UI.Components.Styles;

/// <summary>
/// The application's look, in three states, which is what lets someone try a
/// change before living with it:
/// <list type="bullet">
/// <item><b>Draft</b> — what the editors in Settings are bound to. It is only
/// what has been typed; nothing is looking at it.</item>
/// <item><b>Applied</b> — what every screen is drawn with right now. Apply
/// copies the draft here, so the whole application changes and the person can
/// walk through it and come back: the draft is still here, unsaved, because
/// this object lives as long as the client does.</item>
/// <item><b>Saved</b> — what this device will open with tomorrow. Save writes
/// the applied values to the device's own storage (<c>wslcAgent.style</c>),
/// beside the theme and the page zoom, so the look belongs to the client and
/// two clients of the same agent can differ.</item>
/// </list>
/// Nothing is lost by navigating away: leaving Settings with changes applied
/// but not saved is the point of Apply, not an accident.
/// </summary>
public sealed class UiStyle(IJSRuntime js)
{
    private UiStyleValues _saved = new();

    /// <summary>
    /// What the application ships with: wslc-style.json, read once at startup.
    /// The values compiled into <see cref="UiStyleValues"/> are the same ones,
    /// so a client that cannot read the file looks no different — the file is
    /// there to be changed without building anything.
    /// </summary>
    public UiStyleValues Shipped { get; private set; } = new();

    /// <summary>What the editors write to.</summary>
    public UiStyleValues Draft { get; private set; } = new();

    /// <summary>What every screen is drawn with.</summary>
    public UiStyleValues Applied { get; private set; } = new();

    /// <summary>Raised when Applied changes, for the components that take their parameters from it.</summary>
    public event Action? Changed;

    /// <summary>True while the draft says something the screens are not showing yet.</summary>
    public bool HasPending => !Draft.Matches(Applied);

    /// <summary>True while what the screens show is not what this device will open with.</summary>
    public bool HasUnsaved => !Applied.Matches(_saved);

    private async Task RememberAsync(string json)
    {
        try
        {
            await js.InvokeAsync<string?>("wslcAgent.styleTried", json);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Without it a reload goes back to what was saved, which is safe.
        }
    }

    /// <summary>
    /// What this device remembers, read once when the application starts: what
    /// was saved, and over it what this tab had applied without saving.
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            var shipped = await js.InvokeAsync<string?>("wslcAgent.styleFile");
            if (!string.IsNullOrWhiteSpace(shipped)
                && JsonSerializer.Deserialize<UiStyleValues>(shipped) is { } fromFile)
            {
                Shipped = fromFile;
                _saved = fromFile.Copy();
                Applied = fromFile.Copy();
                Draft = fromFile.Copy();
            }

            var stored = await js.InvokeAsync<string?>("wslcAgent.style");
            if (!string.IsNullOrWhiteSpace(stored)
                && JsonSerializer.Deserialize<UiStyleValues>(stored) is { } values)
            {
                _saved = values;
                Applied = values.Copy();
                Draft = values.Copy();
            }

            var tried = await js.InvokeAsync<string?>("wslcAgent.styleTried");
            if (!string.IsNullOrWhiteSpace(tried)
                && JsonSerializer.Deserialize<UiStyleValues>(tried) is { } applied)
            {
                Applied = applied;
                Draft = applied.Copy();
            }
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException or JsonException)
        {
            // No browser yet, no storage, or something unreadable in it: the
            // application opens with the look it ships with.
        }

        await WriteAsync();
    }

    /// <summary>
    /// The draft becomes what every screen is drawn with. Nothing is saved: it
    /// is kept for this tab alone (the session), so reloading the page finds
    /// the look still on — a reload used to throw away what had just been
    /// applied — and closing the application forgets it.
    /// </summary>
    public async Task ApplyAsync()
    {
        Applied = Draft.Copy();
        await WriteAsync();
        await RememberAsync(JsonSerializer.Serialize(Applied));
        Changed?.Invoke();
    }

    /// <summary>What the screens show becomes what this device opens with.</summary>
    public async Task SaveAsync()
    {
        await ApplyAsync();
        _saved = Applied.Copy();
        // Saved is saved: the tab's copy of what was only tried has nothing
        // left to say.
        await RememberAsync("");
        try
        {
            await js.InvokeAsync<string?>("wslcAgent.style", JsonSerializer.Serialize(_saved));
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            // The look still holds for this session; it is only not remembered.
        }
    }

    /// <summary>A look from somewhere else (an imported file) becomes the draft, ready to be tried.</summary>
    public void Use(UiStyleValues values) => Draft = values.Copy();

    /// <summary>Back to what the application ships with — applied, not saved, so it can be tried first.</summary>
    public async Task ResetAsync()
    {
        Draft = Shipped.Copy();
        await ApplyAsync();
    }

    /// <summary>The CSS variables the stylesheet reads, and what the applied values put in them.</summary>
    /// <summary>
    /// The four metrics a MudBlazor typo is made of. A role that takes another
    /// standard's typo takes all four, or a 16px title would sit on an 11px
    /// line.
    /// </summary>
    private static readonly string[] TypeMetrics = ["size", "weight", "lineheight", "letterspacing"];

    /// <summary>
    /// The application's four type roles and the CSS names each one is written
    /// with. Role three is two names: MudBlazor's caption and subtitle2 are the
    /// same size and the application uses them for the same thing.
    /// </summary>
    private IEnumerable<(string Role, Typo Takes)> TypeRoles() =>
    [
        ("h6", Applied.TitleTypo),
        ("body1", Applied.DefaultTypo),
        ("body2", Applied.TableTypo),
        ("caption", Applied.SecondaryTypo),
        ("subtitle2", Applied.SecondaryTypo),
        ("overline", Applied.OverlineTypo),
    ];

    /// <summary>
    /// A role pointed at its own typo would be a variable defined as itself,
    /// which is a cycle and leaves the property with nothing: those are removed
    /// instead (the empty string), so the theme's own value comes back.
    /// </summary>
    private void AddTypeRoles(Dictionary<string, string> values)
    {
        foreach (var (role, takes) in TypeRoles())
        {
            var pick = takes.ToString();
            foreach (var metric in TypeMetrics)
            {
                values["--mud-typography-" + role + "-" + metric] =
                    pick == role ? "" : "var(--mud-typography-" + pick + "-" + metric + ")";
            }
        }
    }

    private IReadOnlyDictionary<string, string> Variables()
    {
        var values = new Dictionary<string, string>
        {
            ["--wslc-btn-h"] = Px(Applied.ButtonHeight),
            ["--wslc-btn-radius"] = Px(Applied.ButtonRadius),
            ["--wslc-btn-font"] = Px(Applied.ButtonFontSize),
            ["--wslc-btn-pad"] = Px(Applied.ButtonPadding),
            ["--wslc-field-h"] = Px(Applied.FieldHeight),
            ["--wslc-form-pad-top"] = Px(Applied.FormPadTop),
            ["--wslc-form-pad-bottom"] = Px(Applied.FormPadBottom),
            ["--wslc-form-radius"] = Px(Applied.FormFieldRadius),
            ["--wslc-check"] = Px(Applied.CheckSize),
            ["--wslc-row-pad-start"] = Px(Applied.RowPadStart),
            ["--wslc-row-pad-end"] = Px(Applied.RowPadEnd),
            ["--wslc-action-icon"] = Px(Applied.ActionIconSize),
            ["--wslc-action-pad"] = Px(Applied.ActionPadding),
            ["--wslc-bar-icon"] = Px(Applied.BarIconSize),
            ["--wslc-bar-gap"] = Px(Applied.BarGap),
            ["--wslc-tab-h"] = Px(Applied.TabHeight),
            // MudBlazor ships one variable per step (--mud-elevation-1..24), so
            // the header takes the theme's own shadow, not one written here.
            ["--wslc-head-shadow"] = Applied.TabsElevation > 0
                ? "var(--mud-elevation-" + Applied.TabsElevation.ToString(CultureInfo.InvariantCulture) + ")"
                : "none",
        };

        AddTypeRoles(values);
        return values;
    }

    /// <summary>What is not a length: a name the stylesheet matches on the document.</summary>
    private IReadOnlyDictionary<string, string> Flags() => new Dictionary<string, string>
    {
        // For the tables that are not MudBlazor's: the container's file list.
        ["data-wslc-rows"] = string.Join(
            ' ',
            new[]
            {
                Applied.GridDense ? "dense" : "roomy",
                Applied.GridStriped ? "striped" : "plain",
                Applied.GridBordered ? "bordered" : "borderless",
                Applied.GridHover ? "hover" : "still",
                Applied.GridOutlined ? "outlined" : "flat",
            }),
    };

    private static string Px(int value) => value.ToString(CultureInfo.InvariantCulture) + "px";

    private async Task WriteAsync()
    {
        try
        {
            await js.InvokeVoidAsync("wslcAgent.styleVars", Variables());
            await js.InvokeVoidAsync("wslcAgent.styleFlags", Flags());
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Before the web view has loaded there is nothing to paint yet; the
            // next apply writes them.
        }
    }
}
