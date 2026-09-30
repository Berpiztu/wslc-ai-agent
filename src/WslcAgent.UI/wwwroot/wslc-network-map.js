// The network map's one need from the browser: the size of the card it draws
// in, now and whenever it changes (chips wrapping to a second line, a window
// resized). The border box minus the borders, so a scrollbar the drawing itself
// brings does not change the size and set off a redraw loop.

function cardSize(element) {
    const style = getComputedStyle(element);
    const borderX = (parseFloat(style.borderLeftWidth) || 0) + (parseFloat(style.borderRightWidth) || 0);
    const borderY = (parseFloat(style.borderTopWidth) || 0) + (parseFloat(style.borderBottomWidth) || 0);
    return {
        width: Math.round(element.offsetWidth - borderX),
        height: Math.round(element.offsetHeight - borderY),
    };
}

/** Tells `host.OnCardSize(width, height)` the card's size now and on every change; `dispose` stops. */
export function observe(element, host) {
    let last = "";
    const report = () => {
        const { width, height } = cardSize(element);
        const key = `${width}x${height}`;
        if (key === last || width === 0 || height === 0) {
            return;
        }

        last = key;
        host.invokeMethodAsync("OnCardSize", width, height);
    };

    const observer = new ResizeObserver(report);
    observer.observe(element);
    requestAnimationFrame(() => requestAnimationFrame(report));
    return {
        dispose: () => observer.disconnect(),
    };
}
