using MudBlazor;

namespace WslcAgent.UI.Layout;

/// <summary>
/// The MudBlazor theme: the application's palette (navy surfaces,
/// indigo accent) as theme values, and its compact type scale. Everything
/// visual derives from here; components never carry colours of their own.
/// </summary>
public static class WslcTheme
{
    private const string Accent = "#4f46e5";
    private const string Success = "#10b981";
    private const string Warning = "#f59e0b";
    private const string Danger = "#ef4444";

    /// <summary>The accent and warning tones for chart palettes, which MudChart takes as hex strings.</summary>
    public const string AccentHex = Accent;

    public const string WarningHex = Warning;

    public static readonly MudTheme Theme = new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = Accent,
            Secondary = "#818cf8",
            // Magenta: a pink
            // that reads on the dark ground, at full strength (#f472b6, and
            // #ec4899 after it, looked faint in the toolbox).
            Tertiary = "#ff2d95",
            Info = "#38bdf8",
            Success = Success,
            Warning = Warning,
            Error = Danger,
            Background = "#0f172a",
            BackgroundGray = "#243045",
            Surface = "#1e293b",
            AppbarBackground = "#0f172a",
            AppbarText = "#f8fafc",
            DrawerBackground = "#1e293b",
            DrawerText = "#f8fafc",
            DrawerIcon = "#94a3b8",
            TextPrimary = "#f8fafc",
            TextSecondary = "#94a3b8",
            TextDisabled = "#64748b",
            ActionDefault = "#94a3b8",
            ActionDisabled = "#475569",
            ActionDisabledBackground = "#1e293b",
            LinesDefault = "#334155",
            LinesInputs = "#475569",
            TableLines = "#334155",
            TableStriped = "rgba(255, 255, 255, 0.025)",
            TableHover = "rgba(79, 70, 229, 0.12)",
            Divider = "#334155",
            DividerLight = "#243045",
        },
        PaletteLight = new PaletteLight
        {
            Primary = Accent,
            Secondary = "#6366f1",
            // The same magenta, vivid, so it reads on the light ground at full
            // strength (#db2777 looked faint). What stands on it — a filled
            // button's glyph — is MudBlazor's white, on both themes.
            Tertiary = "#e6007e",
            Info = "#0284c7",
            Success = Success,
            Warning = Warning,
            Error = Danger,
            Background = "#f3f6fb",
            BackgroundGray = "#e8edf5",
            Surface = "#ffffff",
            AppbarBackground = "#ffffff",
            AppbarText = "#172033",
            DrawerBackground = "#ffffff",
            DrawerText = "#172033",
            DrawerIcon = "#64748b",
            TextPrimary = "#172033",
            TextSecondary = "#64748b",
            TextDisabled = "#94a3b8",
            ActionDefault = "#64748b",
            // Opaque, like the dark palette's: MudBlazor's own light default is
            // 12% black, so a greyed button laid over a table let the rows read
            // straight through it.
            ActionDisabled = "#94a3b8",
            ActionDisabledBackground = "#e2e8f0",
            LinesDefault = "#dbe3ef",
            LinesInputs = "#cbd5e1",
            TableLines = "#dbe3ef",
            TableStriped = "rgba(15, 23, 42, 0.025)",
            TableHover = "rgba(79, 70, 229, 0.08)",
            Divider = "#dbe3ef",
            DividerLight = "#e8edf5",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",
            DrawerWidthLeft = "200px",
            DrawerMiniWidthLeft = "60px",
            // Two lines at the left of the title bar (the application's name over
            // the session line):
            // MudBlazor's toolbar keeps seven eighths of this.
            AppbarHeight = "72px",
        },
        Typography = TypeScale(),
    };

    /// <summary>
    /// Four steps and no more: 1.06rem for a title, 0.82rem for body and for
    /// buttons, 0.72rem for everything small, 0.68rem for the smallest. Body2,
    /// Subtitle2 and Caption share the third; Button sits on the second, since
    /// MudBlazor's own small-button rule is hardcoded and has to be pointed
    /// back at this scale (see wslc-agent-ui.css). Sizes a third of a pixel
    /// apart are not steps, they are accidents, and every one of them counts as
    /// another type size on the screen.
    /// </summary>
    private static Typography TypeScale()
    {
        string[] family = ["Inter", "Segoe UI", "Roboto", "Helvetica", "Arial", "sans-serif"];
        return new Typography
        {
            Default = new DefaultTypography { FontFamily = family, FontSize = "0.82rem", LineHeight = "1.45" },
            H4 = new H4Typography { FontFamily = family, FontSize = "1.5rem", FontWeight = "600" },
            H5 = new H5Typography { FontFamily = family, FontSize = "1.22rem", FontWeight = "600" },
            H6 = new H6Typography { FontFamily = family, FontSize = "1.06rem", FontWeight = "600", LineHeight = "1.3" },
            Subtitle1 = new Subtitle1Typography { FontFamily = family, FontSize = "0.82rem" },
            Subtitle2 = new Subtitle2Typography { FontFamily = family, FontSize = "0.72rem", FontWeight = "600" },
            Body1 = new Body1Typography { FontFamily = family, FontSize = "0.82rem" },
            Body2 = new Body2Typography { FontFamily = family, FontSize = "0.72rem" },
            Button = new ButtonTypography { FontFamily = family, FontSize = "0.82rem", TextTransform = "none" },
            Caption = new CaptionTypography { FontFamily = family, FontSize = "0.72rem" },
            Overline = new OverlineTypography { FontFamily = family, FontSize = "0.68rem" },
        };
    }
}
