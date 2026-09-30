namespace WslcAgent.UI.Components;

/// <summary>
/// The icon language: Unicode glyphs, wrapped as
/// SVG text so they fit every MudBlazor <c>Icon</c> slot (nav links, menus,
/// buttons). Symbols take the current colour; pictograms keep their own.
/// </summary>
public static class WslcIcons
{
    public static readonly string Home = Symbol("⌂");
    public static readonly string Containers = Pictogram("📦");
    public static readonly string Images = Pictogram("🖼");
    public static readonly string Networks = Pictogram("🌐");
    public static readonly string Volumes = Pictogram("💾");
    public static readonly string Registry = Pictogram("🔑");
    public static readonly string Terminal = Pictogram("💻");
    public static readonly string System = Symbol("⚙");
    public static readonly string Settings = Pictogram("🛠");
    public static readonly string Logs = Pictogram("📋");
    public static readonly string Styles = Pictogram("🎨");

    public static readonly string Session = Symbol("◎");
    public static readonly string Refresh = Symbol("↻");
    public static readonly string Start = Symbol("▶");
    public static readonly string Stop = Symbol("■");
    public static readonly string More = Symbol("⋮");
    public static readonly string Remove = Pictogram("🗑");
    public static readonly string Create = Symbol("＋");

    public static readonly string Details = Pictogram("🗂");
    public static readonly string ViewEdit = Pictogram("🔍");
    public static readonly string Packages = Pictogram("📦");
    public static readonly string Copy = Pictogram("📋");
    public static readonly string Debug = Pictogram("🐞");
    public static readonly string Files = Pictogram("📁");
    public static readonly string Drive = Pictogram("🖴");
    public static readonly string Restart = Pictogram("🔄");

    public static readonly string Pull = Symbol("⬇");
    public static readonly string Push = Symbol("⬆");
    public static readonly string Save = Pictogram("💾");
    public static readonly string Tag = Pictogram("🏷");
    public static readonly string Build = Pictogram("🔨");
    public static readonly string Import = Pictogram("📥");
    public static readonly string Load = Pictogram("📦");
    public static readonly string Hub = Pictogram("🔍");
    public static readonly string Prune = Pictogram("🧹");
    public static readonly string Connect = Pictogram("🔗");
    public static readonly string Disconnect = Symbol("⛓");
    public static readonly string Map = Pictogram("🗺");
    public static readonly string Search = Pictogram("🔍");
    public static readonly string Wrap = Pictogram("↩️");
    public static readonly string Check = Pictogram("✅");
    public static readonly string Reload = Pictogram("🔃");
    public static readonly string JumpEnd = Pictogram("⏬");
    public static readonly string Clear = Pictogram("🧹");
    public static readonly string Cancel = Symbol("✕");
    public static readonly string Kill = Symbol("⚡");
    public static readonly string Stats = Pictogram("📊");
    public static readonly string Export = Pictogram("🧾");
    public static readonly string Backup = Pictogram("📦");
    public static readonly string Browser = Pictogram("🌐");
    public static readonly string Sessions = Pictogram("👁");

    // The Files browser's glyphs.
    public static readonly string Cut = Symbol("✂");
    public static readonly string Edit = Symbol("✎");
    public static readonly string Rename = Symbol("↔");
    public static readonly string Download = Symbol("⬇");
    public static readonly string Duplicate = Symbol("⧉");
    public static readonly string Upload = Symbol("⬆");
    public static readonly string Up = Symbol("↑");
    public static readonly string UpFolder = UpArrowOver(Files);

    /// <summary>A plain symbol (⌂ ◎ ↻ ▶ ■ ⋮): monochrome font first, so it takes the current colour.</summary>
    private static string Symbol(string text) =>
        Glyph(text, "Segoe UI Symbol, Apple Symbols, Segoe UI Emoji, sans-serif");

    /// <summary>A pictogram (📦 🗑 🔄): colour emoji font first, so it keeps its colours.</summary>
    private static string Pictogram(string text) =>
        Glyph(text, "Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji, Segoe UI Symbol, sans-serif");

    /// <summary>
    /// A folder with the classic "up one level" arrow drawn on it, as every
    /// file manager shows it: the glyph itself, with the arrow over its body so
    /// it reads at the row's own type size.
    /// </summary>
    private static string UpArrowOver(string glyph) =>
        glyph + """<path d="M12 8.6 L16.4 13.4 H13.7 V17.6 H10.3 V13.4 H7.6 Z" fill="#1b2130" stroke="#ffffff" stroke-width="1.1" stroke-linejoin="round" paint-order="stroke"/>""";

    /// <summary>One glyph centred in MudBlazor's 24-unit icon box.</summary>
    private static string Glyph(string text, string fonts) =>
        $"""<text x="12" y="18" text-anchor="middle" font-size="16" font-family="{fonts}" fill="currentColor">{text}</text>""";
}
