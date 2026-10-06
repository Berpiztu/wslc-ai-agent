// Berpiztu's dashboard: what only the browser can answer, and nothing else;
// C# first, JavaScript only where C# cannot. Everything the dashboard decides is
// C# (DashboardInterop); these only measure — once, or as the window turns —
// and hold the pointer.

// Where the element stands and how large it is, from the viewport's corner:
// what turns a pointer's position into a cell of the canvas.
export function measure(element) {
    const box = element.getBoundingClientRect();
    return { left: box.left, top: box.top, width: box.width, height: box.height };
}

// The pointer's events keep going to the element while the drag lasts,
// wherever the pointer goes; a pointer already released has nothing to keep.
export function capture(element, pointerId) {
    try {
        element.setPointerCapture(pointerId);
    } catch {
        // Released before the capture arrived: the drag is already over.
    }
}

// The room inside the element as if it showed no scrollbar, and how thick one
// of its scrollbars is: the same answer whether a scrollbar shows right now or
// not. A view fitted on the room a scrollbar left was refitted when the
// scrollbar went, grew until it came back, and shook for as long as the window
// stood at that size.
export function measureInside(element) {
    const style = getComputedStyle(element);
    const across = parseFloat(style.borderLeftWidth) + parseFloat(style.borderRightWidth);
    const down = parseFloat(style.borderTopWidth) + parseFloat(style.borderBottomWidth);
    return { width: element.offsetWidth - across, height: element.offsetHeight - down, bar: scrollbarOf(element) };
}

// A scrollbar's thickness as the element draws one: a hidden box of its own
// classes, which always shows its scrollbars, measured beside it.
function scrollbarOf(element) {
    const probe = document.createElement('div');
    probe.className = element.className;
    probe.style.cssText = 'position: absolute; top: 0; left: 0; width: 100px; height: 100px; overflow: scroll; visibility: hidden; flex: none; border: 0;';
    element.parentElement.appendChild(probe);
    const bar = probe.offsetWidth - probe.clientWidth;
    probe.remove();
    return bar;
}

// That the element's size changed, told to C# once it has stood still a tenth
// of a second — a window resized, a phone turned, a pane opened beside it — so
// the dashboard's zoom fits it again.
// Answers what stops it.
export function watchSize(element, receiver) {
    let settling;
    const observer = new ResizeObserver(() => {
        clearTimeout(settling);
        settling = setTimeout(() => receiver.invokeMethodAsync('Resized'), 100);
    });
    observer.observe(element);
    return {
        stop() {
            clearTimeout(settling);
            observer.disconnect();
        },
    };
}

// A finger held still on the element — the canvas: its floor, an object, a
// card; not a handle, which answers at once — for half a second, told to C#
// with where it is and its pointer: in design a finger's swipe scrolls the
// dashboard, and held still it takes what it is on. A finger that moves first is a scroll, the
// browser's. Once held, the browser is kept from scrolling until the finger is
// lifted, so the drag that follows moves what was taken or draws the
// rectangle. Answers what stops it.
export function watchHold(element, receiver) {
    const hold = 500;
    const still = 8;
    let timer = null;
    let start = null;
    let held = false;
    const forget = () => {
        clearTimeout(timer);
        timer = null;
        held = false;
    };
    const down = event => {
        if (event.pointerType !== 'touch' || event.target.closest('.bz-dash-handle')) {
            return;
        }

        forget();
        start = { x: event.clientX, y: event.clientY, id: event.pointerId };
        timer = setTimeout(() => {
            timer = null;
            held = true;
            receiver.invokeMethodAsync('Held', start.x, start.y, start.id);
        }, hold);
    };
    const move = event => {
        if (timer !== null && Math.hypot(event.clientX - start.x, event.clientY - start.y) > still) {
            clearTimeout(timer);
            timer = null;
        }
    };
    const scroll = event => {
        if (held && event.cancelable) {
            event.preventDefault();
        }
    };
    element.addEventListener('pointerdown', down);
    element.addEventListener('pointermove', move);
    element.addEventListener('pointerup', forget);
    element.addEventListener('pointercancel', forget);
    element.addEventListener('touchmove', scroll, { passive: false });
    return {
        stop() {
            forget();
            element.removeEventListener('pointerdown', down);
            element.removeEventListener('pointermove', move);
            element.removeEventListener('pointerup', forget);
            element.removeEventListener('pointercancel', forget);
            element.removeEventListener('touchmove', scroll);
        },
    };
}

// Whether the window is taller than it is wide, told to C# now and each time
// that changes, a phone turned or a window reshaped: the screen's orientation
// chooses the view. Answers what stops it.
export function watchOrientation(receiver) {
    const portrait = window.matchMedia('(orientation: portrait)');
    const told = () => receiver.invokeMethodAsync('Turned', portrait.matches);
    portrait.addEventListener('change', told);
    told();
    return {
        stop() {
            portrait.removeEventListener('change', told);
        },
    };
}
