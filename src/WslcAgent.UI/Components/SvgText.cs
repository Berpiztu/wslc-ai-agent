using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace WslcAgent.UI.Components;

/// <summary>
/// An SVG <c>&lt;text&gt;</c> element: Razor reserves the <c>text</c> tag for its
/// own use, so a drawing (the network map) writes its labels through this.
/// </summary>
public sealed class SvgText : ComponentBase
{
    [Parameter, EditorRequired] public string Value { get; set; } = "";

    /// <summary>Every SVG attribute of the element: x, y, text-anchor, style, font-size…</summary>
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? Attributes { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "text");
        builder.AddMultipleAttributes(1, Attributes);
        builder.AddContent(2, Value);
        builder.CloseElement();
    }
}
