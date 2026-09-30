using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Berpiztu.Dashboard.Designer;

/// <summary>An element's box on the screen, in CSS pixels from the viewport's corner.</summary>
public sealed record ScreenBox(double Left, double Top, double Width, double Height)
{
    public bool Contains(double x, double y) => x >= Left && x < Left + Width && y >= Top && y < Top + Height;
}

/// <summary>
/// The SDK's JavaScript, and all of it: what only the browser can answer. Where an element stands and how large it
/// is, when its size changes, whether the window is taller than it is wide
/// as it turns, when a finger is held still on the canvas's floor, and keeping
/// the pointer on an element while a drag lasts, so the drag
/// does not end when the pointer leaves it. Everything else — which cell a
/// point falls on, whether an object fits, moving, resizing — is C#.
/// </summary>
public sealed class DashboardInterop(IJSRuntime js) : IAsyncDisposable
{
    private Task<IJSObjectReference>? _module;

    private Task<IJSObjectReference> Module =>
        _module ??= js.InvokeAsync<IJSObjectReference>("import", "./_content/Berpiztu.Dashboard/berpiztu-dashboard.js").AsTask();

    public async Task<ScreenBox> MeasureAsync(ElementReference element) =>
        await (await Module).InvokeAsync<ScreenBox>("measure", element);

    /// <summary>The room inside an element, its scrollbars left out; its width and height alone, from no corner.</summary>
    public async Task<ScreenBox> MeasureInsideAsync(ElementReference element) =>
        await (await Module).InvokeAsync<ScreenBox>("measureInside", element);

    /// <summary>
    /// Tells <paramref name="turned"/> whether the window is taller than it
    /// is wide, at once and each time that changes, until the answer is
    /// disposed: the orientation that chooses the dashboard's view.
    /// </summary>
    public async Task<IAsyncDisposable> WatchOrientationAsync(Func<bool, Task> turned)
    {
        var receiver = DotNetObjectReference.Create(new OrientationReceiver(turned));
        var watch = await (await Module).InvokeAsync<IJSObjectReference>("watchOrientation", receiver);
        return new Watch<OrientationReceiver>(watch, receiver);
    }

    /// <summary>
    /// Tells <paramref name="resized"/> each time <paramref name="element"/>'s
    /// size changes, once it has stood still, until the answer is disposed:
    /// what the dashboard's zoom fits.
    /// </summary>
    public async Task<IAsyncDisposable> WatchSizeAsync(ElementReference element, Func<Task> resized)
    {
        var receiver = DotNetObjectReference.Create(new SizeReceiver(resized));
        var watch = await (await Module).InvokeAsync<IJSObjectReference>("watchSize", element, receiver);
        return new Watch<SizeReceiver>(watch, receiver);
    }

    /// <summary>
    /// Tells <paramref name="held"/> where a finger was held still on
    /// <paramref name="element"/> itself, and its pointer, until the answer is
    /// disposed: in design a finger on the floor scrolls, and held still it
    /// begins a selection, the browser kept from scrolling while it lasts.
    /// </summary>
    public async Task<IAsyncDisposable> WatchHoldAsync(ElementReference element, Func<double, double, long, Task> held)
    {
        var receiver = DotNetObjectReference.Create(new HoldReceiver(held));
        var watch = await (await Module).InvokeAsync<IJSObjectReference>("watchHold", element, receiver);
        return new Watch<HoldReceiver>(watch, receiver);
    }

    /// <summary>What the browser tells when a finger is held still.</summary>
    private sealed class HoldReceiver(Func<double, double, long, Task> held)
    {
        [JSInvokable]
        public Task Held(double x, double y, long pointerId) => held(x, y, pointerId);
    }

    /// <summary>What the browser tells when the element's size changes.</summary>
    private sealed class SizeReceiver(Func<Task> resized)
    {
        [JSInvokable]
        public Task Resized() => resized();
    }

    /// <summary>What the browser tells when the window turns.</summary>
    private sealed class OrientationReceiver(Func<bool, Task> turned)
    {
        [JSInvokable]
        public Task Turned(bool portrait) => turned(portrait);
    }

    /// <summary>A watch of a width or of the window's orientation, stopped and let go of when disposed.</summary>
    private sealed class Watch<TReceiver>(IJSObjectReference watch, DotNetObjectReference<TReceiver> receiver) : IAsyncDisposable
        where TReceiver : class
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await watch.InvokeVoidAsync("stop");
                await watch.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page is gone, and the watch with it.
            }

            receiver.Dispose();
        }
    }

    /// <summary>The pointer's events go to <paramref name="element"/> until it is released, wherever the pointer goes.</summary>
    public async Task CaptureAsync(ElementReference element, long pointerId) =>
        await (await Module).InvokeVoidAsync("capture", element, pointerId);

    public async ValueTask DisposeAsync()
    {
        if (_module is { IsCompletedSuccessfully: true })
        {
            try
            {
                await _module.Result.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page is gone, and the module with it.
            }
        }
    }
}
