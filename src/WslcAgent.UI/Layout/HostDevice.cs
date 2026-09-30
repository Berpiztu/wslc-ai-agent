using Microsoft.JSInterop;

namespace WslcAgent.UI.Layout;

/// <summary>
/// Whether the UI is being held in the hand: on a phone or a tablet the toasts
/// come down at the top centre, where the eye already is, instead of the top
/// right corner of a desktop window (<c>Toasts</c>). The browser answers for
/// every host, the Android WebView included, so there is no per-platform
/// implementation.
/// </summary>
public sealed class HostDevice(IJSRuntime js)
{
    private bool? _handheld;

    /// <summary>
    /// True on a touch-first host; false with a mouse. Asked once and cached.
    /// The Windows client is a desktop application, whatever its WebView says
    /// of the pointer: a touch screen, or a virtual machine's input device,
    /// can make the WebView report a coarse one on a desktop driven with a
    /// mouse. Only there does the UI run on Windows itself; in a browser it
    /// runs in WebAssembly and the browser answers.
    /// </summary>
    public async ValueTask<bool> IsHandheldAsync()
    {
        _handheld ??= !OperatingSystem.IsWindows() && await js.InvokeAsync<bool>("wslcAgent.isHandheld");
        return _handheld.Value;
    }
}
