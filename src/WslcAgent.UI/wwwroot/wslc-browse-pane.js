// The host browser pane: a browser on the agent's machine showing a container's
// published port, painted here from its screencast, with this device's pointer,
// keys and clipboard sent back (BrowseStream, protocol 2). Its toolbar is the
// Blazor component around this surface (BrowserPane.razor), which calls the
// verbs below.
//
// One rule: the hole is the truth. The browser's viewport is the surface's size
// in CSS px, on every platform and at all times (open, resume, rotate, the soft
// keyboard), and the picture is painted 1:1, never scaled. The keyboard shrinks
// the document, the surface shrinks, the page reflows.

const PROTOCOL = 2;
const SYNC_DEBOUNCE_MS = 100;
const FINGER_PAN_PX = 10;
const LONG_PRESS_MS = 550;
// Wheel and finger-pan deltas are summed and sent at most this often: a wheel
// fires 20-30 events a second, each two DevTools calls on the host.
const WHEEL_MS = 60;
const EXTEND_THROTTLE_MS = 60;
// Pause between the handles landing and the menu appearing, and the gap between
// the selection and the menu.
const MENU_DELAY_MS = 50;
const MENU_GAP_PX = 40;

// The server's clamp (BrowseTarget); below the settled size a dialog is still laying out.
const MIN_W = 320;
const MIN_H = 240;
const MAX_W = 3840;
const MAX_H = 2160;
const SETTLED_MIN_W = 400;
const SETTLED_MIN_H = 280;

const SPECIAL_KEYS = new Set([
    "Backspace", "Delete", "Enter", "Tab", "Escape", "ArrowLeft", "ArrowRight",
    "ArrowUp", "ArrowDown", "Home", "End", "PageUp", "PageDown",
]);

const INPUT_MODES = { number: "decimal", tel: "tel", email: "email", url: "url", search: "search" };

function clamp(value, lo, hi) {
    const n = Number(value);
    return Number.isFinite(n) ? Math.max(lo, Math.min(hi, Math.floor(n))) : lo;
}

/** Reconnect delay: 1 s, 2 s, 4 s … capped at 15 s. */
function backoffMs(attempt) {
    return Math.min(15000, 1000 * Math.pow(2, Math.max(0, Number(attempt) || 0)));
}

/** Wheel deltas in CSS px, whatever unit the device reported. */
function normalizeWheel(event, hole) {
    let dx = Number(event.deltaX) || 0;
    let dy = Number(event.deltaY) || 0;
    if (event.deltaMode === 1) {
        dx *= 16;
        dy *= 16;
    } else if (event.deltaMode === 2) {
        dx *= hole.width || 800;
        dy *= hole.height || 600;
    }
    return { dx, dy };
}

/** A per-device id, so two clients do not share one host browser and a reopen resumes this one's. */
function viewerId() {
    const key = "wslc.browse.viewerId";
    try {
        let id = localStorage.getItem(key) || "";
        if (!/^[A-Za-z0-9_-]{8,64}$/.test(id)) {
            id = (window.crypto && typeof window.crypto.randomUUID === "function"
                ? window.crypto.randomUUID().replace(/-/g, "")
                : "v" + String(Date.now()) + Math.random().toString(36).slice(2, 10)).slice(0, 32);
            localStorage.setItem(key, id);
        }
        return id;
    } catch {
        return "ephemeral" + String(Date.now()).slice(-10);
    }
}

/**
 * Opens the pane on `surface` (which holds the canvas, the IME field and the
 * placeholder) against the stream at `streamUrl`. `addressHost` is the address
 * field's wrapper: while it has the focus, the page's address does not
 * overwrite what is being typed. `dotnet` receives OnStatus, OnAddress,
 * OnReady, OnZoom and OnEnded.
 */
export function open(surface, addressHost, streamUrl, sessionId, dotnet) {
    const canvas = surface.querySelector("canvas");
    const ctx = canvas.getContext("2d");
    const ime = surface.querySelector("textarea");
    const placeholder = surface.querySelector("[data-placeholder]");
    const cleanups = [];

    let ws = null;
    let intentionalClose = false;
    let viewportW = 0;
    let viewportH = 0;
    let reconnectAttempt = 0;
    let reconnectTimer = null;
    let everReady = false;
    let syncTimer = null;
    let frameCount = 0;
    let frameBytes = 0;
    let frameSince = performance.now();
    let imeComposing = false;
    let lastAddress = "";
    let statusState = "idle";
    let disposed = false;

    const listen = (target, type, handler, options) => {
        target.addEventListener(type, handler, options);
        cleanups.push(() => target.removeEventListener(type, handler, options));
    };

    const notify = (method, ...args) => {
        if (!disposed) {
            dotnet.invokeMethodAsync(method, ...args).catch(() => {});
        }
    };

    function setStatus(text, state) {
        statusState = state || "idle";
        const size = viewportW && viewportH ? " · " + viewportW + "×" + viewportH : "";
        notify("OnStatus", text + (state === "ok" ? size : ""), statusState);
    }

    /** One second of picture: how many frames came in and what they weighed. */
    function countFrame(bytes) {
        frameCount += 1;
        frameBytes += bytes;
        const now = performance.now();
        if (now - frameSince < 1000) {
            return;
        }

        const seconds = (now - frameSince) / 1000;
        notify("OnRate", Math.round(frameCount / seconds), Math.round(frameBytes / seconds / 1024));
        frameCount = 0;
        frameBytes = 0;
        frameSince = now;
    }

    function showPlaceholder(text) {
        placeholder.hidden = false;
        placeholder.textContent = text;
    }

    function setAddress(url) {
        if (url && url !== lastAddress && !addressHost.contains(document.activeElement)) {
            lastAddress = url;
            notify("OnAddress", url);
        }
    }

    function send(payload) {
        if (!ws || ws.readyState !== WebSocket.OPEN) {
            return false;
        }
        ws.send(JSON.stringify(payload));
        return true;
    }

    // ------------------------------------------------------------- geometry

    /** The hole: the surface's own box. Nothing else is consulted. */
    function measureHole() {
        return {
            width: clamp(surface.clientWidth, MIN_W, MAX_W),
            height: clamp(surface.clientHeight, MIN_H, MAX_H),
        };
    }

    /** Waits until the hole is a real layout: a dialog starts tiny. */
    function whenPaneSized(done) {
        let n = 0;
        let lastW = 0;
        let lastH = 0;
        let stable = 0;
        const tick = () => {
            const hole = measureHole();
            if (hole.width === lastW && hole.height === lastH && hole.width >= SETTLED_MIN_W && hole.height >= SETTLED_MIN_H) {
                stable += 1;
            } else {
                stable = 0;
                lastW = hole.width;
                lastH = hole.height;
            }
            if (stable >= 3 || n >= 90) {
                done(hole);
                return;
            }
            n += 1;
            window.requestAnimationFrame(tick);
        };
        window.requestAnimationFrame(tick);
    }

    /** The hole changed: the browser resizes. The only path that changes the viewport. */
    function syncViewport() {
        const hole = measureHole();
        if (hole.width === viewportW && hole.height === viewportH) {
            return;
        }
        viewportW = hole.width;
        viewportH = hole.height;
        send({ type: "resize", width: viewportW, height: viewportH });
        scheduleSelectionRefresh(300);
        if (statusState === "ok") {
            setStatus("Connected", "ok");
        }
    }

    function scheduleSync() {
        if (syncTimer) {
            window.clearTimeout(syncTimer);
        }
        syncTimer = window.setTimeout(() => {
            syncTimer = null;
            syncViewport();
        }, SYNC_DEBOUNCE_MS);
    }

    /** Paints the JPEG at its own pixel size: CSS box == bitmap == browser px. */
    function drawFrame(source, deviceW, deviceH) {
        const w = Number(deviceW) || source.naturalWidth || source.width;
        const h = Number(deviceH) || source.naturalHeight || source.height;
        if (!w || !h) {
            return;
        }
        if (canvas.width !== w || canvas.height !== h) {
            canvas.width = w;
            canvas.height = h;
            canvas.style.width = w + "px";
            canvas.style.height = h + "px";
        }
        ctx.drawImage(source, 0, 0, w, h);
        canvas.hidden = false;
        placeholder.hidden = true;
    }

    // Frames decode off the main thread, and only the newest undecoded one is
    // kept: a slow device shows the latest picture instead of a decode queue.
    let pendingFrame = null;
    let decoding = false;

    function queueFrame(blob, header) {
        pendingFrame = { blob, header };
        if (!decoding) {
            drainFrames();
        }
    }

    function drainFrames() {
        const next = pendingFrame;
        pendingFrame = null;
        if (!next || disposed) {
            decoding = false;
            return;
        }
        decoding = true;
        const done = () => {
            decoding = false;
            if (pendingFrame) {
                drainFrames();
            }
        };
        if (typeof createImageBitmap === "function") {
            createImageBitmap(next.blob)
                .then((bmp) => {
                    drawFrame(bmp, next.header.width, next.header.height);
                    if (bmp.close) {
                        bmp.close();
                    }
                })
                .catch(() => {})
                .then(done);
            return;
        }
        const url = URL.createObjectURL(next.blob);
        const img = new Image();
        img.onload = () => {
            drawFrame(img, next.header.width, next.header.height);
            URL.revokeObjectURL(url);
            done();
        };
        img.onerror = () => {
            URL.revokeObjectURL(url);
            done();
        };
        img.src = url;
    }

    /** u32 big-endian header length, JSON header, JPEG bytes. */
    function onBinaryMessage(buffer) {
        if (!(buffer instanceof ArrayBuffer) || buffer.byteLength < 4) {
            return;
        }
        const n = new DataView(buffer).getUint32(0);
        if (n <= 0 || 4 + n > buffer.byteLength) {
            return;
        }
        let header;
        try {
            header = JSON.parse(new TextDecoder().decode(new Uint8Array(buffer, 4, n)));
        } catch {
            return;
        }
        countFrame(buffer.byteLength);
        queueFrame(new Blob([new Uint8Array(buffer, 4 + n)], { type: header.mime || "image/jpeg" }), header);
        setAddress(header.url);
    }

    // ---------------------------------------------------------------- input

    /**
     * How much larger than its own CSS px an element is drawn: the app's page zoom
     * (--wslc-zoom) scales the pane, while the host browser is sized in the
     * unzoomed px the surface measures itself in.
     */
    function zoomOf(el) {
        return el.offsetWidth ? el.getBoundingClientRect().width / el.offsetWidth : 1;
    }

    /** A client point in browser CSS px: the canvas origin off and the page zoom undone. Null outside the picture. */
    function canvasPoint(clientX, clientY) {
        if (canvas.hidden) {
            return null;
        }
        const rect = canvas.getBoundingClientRect();
        const zoom = zoomOf(canvas);
        const x = (clientX - rect.left) / zoom;
        const y = (clientY - rect.top) / zoom;
        return x < 0 || y < 0 || x > rect.width / zoom || y > rect.height / zoom ? null : { x, y };
    }

    function sendPointer(clientX, clientY, action, button, clickCount, clampToFrame) {
        let p = canvasPoint(clientX, clientY);
        if (!p && clampToFrame && !canvas.hidden) {
            // A drag past the edge stays alive at the border.
            const r = canvas.getBoundingClientRect();
            const zoom = zoomOf(canvas);
            p = {
                x: Math.max(0, Math.min(r.width, clientX - r.left)) / zoom,
                y: Math.max(0, Math.min(r.height, clientY - r.top)) / zoom,
            };
        }
        if (!p) {
            return;
        }
        const payload = { type: "pointer", action, x: p.x, y: p.y, button: button || "left" };
        if (clickCount) {
            payload.clickCount = clickCount;
        }
        send(payload);
    }

    let wheelPending = null;
    let wheelTimer = 0;

    function sendWheel(clientX, clientY, dx, dy) {
        if (wheelPending) {
            wheelPending.dx += dx;
            wheelPending.dy += dy;
            wheelPending.clientX = clientX;
            wheelPending.clientY = clientY;
        } else {
            wheelPending = { clientX, clientY, dx, dy };
        }
        if (!wheelTimer) {
            wheelTimer = window.setTimeout(flushWheel, WHEEL_MS);
        }
    }

    function flushWheel() {
        wheelTimer = 0;
        const w = wheelPending;
        wheelPending = null;
        if (!w || (!w.dx && !w.dy)) {
            return;
        }
        const p = canvasPoint(w.clientX, w.clientY);
        if (p) {
            send({ type: "pointer", action: "wheel", x: p.x, y: p.y, deltaX: w.dx, deltaY: w.dy });
        }
    }

    function focusQuietly(element) {
        try {
            element.focus({ preventScroll: true });
        } catch {
            element.focus();
        }
    }

    /**
     * Parks the 1×1 IME field at the tap and focuses it: that is what opens the
     * soft keyboard, which then shrinks the document and the surface with it.
     */
    function focusIme(clientX, clientY) {
        const rect = surface.getBoundingClientRect();
        const zoom = zoomOf(surface);
        ime.style.left = Math.round(Math.max(0, Math.min(rect.width - 2, clientX - rect.left)) / zoom) + "px";
        ime.style.top = Math.round(Math.max(0, Math.min(rect.height - 2, clientY - rect.top)) / zoom) + "px";
        focusQuietly(ime);
    }

    /** Where the last tap or click landed; the `focus` reply decides the keyboard. */
    let lastTap = { x: 0, y: 0 };

    /** The host said what took focus: a text field gets the keyboard, anything else gives it back. */
    function onFocusReply(msg) {
        if (msg.editable) {
            ime.inputMode = INPUT_MODES[String(msg.inputType || "")] || "text";
            focusIme(lastTap.x, lastTap.y);
            return;
        }
        if (document.activeElement === ime) {
            ime.blur();
        }
        focusQuietly(surface);
    }

    let finger = null;
    let mouseDrag = null;

    const isFinger = (event) => event.pointerType === "touch" || event.pointerType === "pen";
    const mouseClicks = (event) => Math.max(1, Math.min(3, Number(event.detail) || 1));
    const capture = (element, id) => {
        try {
            element.setPointerCapture(id);
        } catch {
            // The pointer is already gone.
        }
    };
    const release = (element, id) => {
        try {
            element.releasePointerCapture(id);
        } catch {
            // Not captured.
        }
    };

    listen(canvas, "pointerdown", (event) => {
        if (event.button !== 0 && event.button !== 2) {
            return;
        }
        event.preventDefault();
        closeMenu();
        const button = event.button === 2 ? "right" : "left";
        if (!isFinger(event)) {
            // A mouse sends real down/move/up, so the page gets drag selection
            // and double-click word selection, not just clicks.
            mouseDrag = { id: event.pointerId, button, clicks: mouseClicks(event) };
            capture(canvas, event.pointerId);
            sendPointer(event.clientX, event.clientY, "down", button, mouseDrag.clicks);
            return;
        }
        // A finger is a tap or a pan, decided when it moves; a pan is a wheel.
        const g = {
            id: event.pointerId,
            startX: event.clientX,
            startY: event.clientY,
            lastX: event.clientX,
            lastY: event.clientY,
            moved: false,
            menu: false,
            inSelection: pointInSelection(event.clientX, event.clientY),
            button,
            timer: null,
        };
        finger = g;
        g.timer = window.setTimeout(() => {
            g.timer = null;
            if (finger === g && !g.moved) {
                // A long press selects the word, as on a phone: a double-click on
                // the host. The menu comes on release.
                g.menu = true;
                sendPointer(g.startX, g.startY, "click", "left", 2);
                window.setTimeout(requestSelection, 120);
            }
        }, LONG_PRESS_MS);
        capture(canvas, event.pointerId);
    });

    listen(canvas, "pointermove", (event) => {
        if (mouseDrag && event.pointerId === mouseDrag.id) {
            sendPointer(event.clientX, event.clientY, "move", mouseDrag.button, mouseDrag.clicks, true);
            return;
        }
        if (!finger || event.pointerId !== finger.id) {
            return;
        }
        event.preventDefault();
        const dx = event.clientX - finger.lastX;
        const dy = event.clientY - finger.lastY;
        finger.lastX = event.clientX;
        finger.lastY = event.clientY;
        if (!finger.moved) {
            if (Math.hypot(event.clientX - finger.startX, event.clientY - finger.startY) < FINGER_PAN_PX) {
                return;
            }
            finger.moved = true;
            if (finger.timer) {
                window.clearTimeout(finger.timer);
                finger.timer = null;
            }
        }
        if (finger.menu) {
            return;
        }
        // The content follows the finger: moving up scrolls down.
        sendWheel(finger.startX, finger.startY, -dx, -dy);
        scheduleSelectionRefresh(150);
    });

    const endFinger = (event) => {
        if (!finger || event.pointerId !== finger.id) {
            return null;
        }
        const g = finger;
        finger = null;
        if (g.timer) {
            window.clearTimeout(g.timer);
            g.timer = null;
        }
        release(canvas, event.pointerId);
        return g;
    };

    listen(canvas, "pointerup", (event) => {
        if (mouseDrag && event.pointerId === mouseDrag.id) {
            const d = mouseDrag;
            mouseDrag = null;
            release(canvas, event.pointerId);
            lastTap = { x: event.clientX, y: event.clientY };
            sendPointer(event.clientX, event.clientY, "up", d.button, d.clicks, true);
            // A drag or a double-click may just have made a selection: ask even
            // when no handles are showing.
            window.setTimeout(requestSelection, 120);
            return;
        }
        const g = endFinger(event);
        if (!g) {
            return;
        }
        event.preventDefault();
        if (g.menu) {
            // The menu follows the handles; this point is the fallback.
            queueMenu(g.startX + 12, g.startY + MENU_GAP_PX);
            return;
        }
        if (!g.moved) {
            if (g.inSelection) {
                queueMenu(g.startX + 12, g.startY + MENU_GAP_PX);
                requestSelection();
                return;
            }
            lastTap = { x: g.startX, y: g.startY };
            sendPointer(g.startX, g.startY, "click", g.button);
            scheduleSelectionRefresh(150);
        }
    });

    listen(canvas, "pointercancel", (event) => {
        if (mouseDrag && event.pointerId === mouseDrag.id) {
            sendPointer(event.clientX, event.clientY, "up", mouseDrag.button, mouseDrag.clicks, true);
            mouseDrag = null;
            return;
        }
        endFinger(event);
    });

    listen(canvas, "contextmenu", (event) => {
        event.preventDefault();
        if (!isFinger(event)) {
            // A finger's long press has its own timer.
            showMenu(event.clientX, event.clientY);
        }
    });

    listen(canvas, "wheel", (event) => {
        event.preventDefault();
        const d = normalizeWheel(event, { width: viewportW, height: viewportH });
        sendWheel(event.clientX, event.clientY, d.dx, d.dy);
        scheduleSelectionRefresh(150);
    }, { passive: false });

    // A press on the surface outside the picture: keys go to the surface.
    listen(surface, "pointerdown", (event) => {
        if (event.target !== canvas && event.target !== ime) {
            focusQuietly(surface);
        }
    });

    function onKeyDown(event) {
        const key = event.key;
        if (key === "Process" || key === "Unidentified") {
            return;
        }
        if (SPECIAL_KEYS.has(key)) {
            event.preventDefault();
            event.stopPropagation();
            send({ type: "key", key });
            scheduleSelectionRefresh(120);
            return;
        }
        if (!(event.ctrlKey || event.metaKey) || key.length !== 1) {
            return;
        }
        const lower = key.toLowerCase();
        if (lower === "v") {
            // The browser pastes into the IME field and the paste handler sends
            // the text: Ctrl+V on the host would paste the host's clipboard.
            return;
        }
        if (lower === "c" || lower === "x") {
            event.preventDefault();
            requestCopy();
            if (lower === "x") {
                send({ type: "key", key: "Control+x" });
            }
            hideHandles();
            return;
        }
        // Select all, undo, redo… go to the host as keys.
        event.preventDefault();
        send({ type: "key", key: (event.ctrlKey ? "Control+" : "Meta+") + key });
    }

    /** Asks for the host's selection; it comes back as `clipboard`. */
    function requestCopy() {
        if (!send({ type: "copy" })) {
            setStatus("Not connected", "error");
        }
    }

    function writeClientClipboard(text) {
        if (!text) {
            setStatus("Nothing selected", "ok");
            return;
        }
        const done = () => setStatus("Copied", "ok");
        const fallback = () => {
            // WebViews without navigator.clipboard copy through a selected field.
            ime.value = text;
            ime.select();
            let ok = false;
            try {
                ok = document.execCommand("copy");
            } catch {
                ok = false;
            }
            ime.value = "";
            if (ok) {
                done();
            } else {
                setStatus("Copy blocked by browser", "error");
            }
        };
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(done, fallback);
        } else {
            fallback();
        }
    }

    function pasteFromClientClipboard() {
        const refused = () => setStatus("Paste: use Ctrl+V or the keyboard's paste", "error");
        if (!navigator.clipboard || !navigator.clipboard.readText) {
            refused();
            return;
        }
        navigator.clipboard.readText().then((text) => {
            if (text) {
                send({ type: "key", text });
            }
        }, refused);
    }

    // ----------------------------------------------------- selection handles

    let menuPending = false;
    let menuTimer = null;
    let menuFallback = { x: 0, y: 0 };
    let handleStart = null;
    let handleEnd = null;
    let handleDrag = null;
    let extendTimer = null;
    let extendPending = null;
    let refreshTimer = null;
    /** The last selection as a box in canvas px (several lines take the full width). */
    let selectionBox = null;

    function makeHandle(which) {
        const el = document.createElement("div");
        el.className = "wslc-browse-handle wslc-browse-handle-" + which;
        el.setAttribute("aria-hidden", "true");
        el.addEventListener("pointerdown", (event) => {
            event.preventDefault();
            event.stopPropagation();
            closeMenu();
            handleDrag = { which, id: event.pointerId };
            capture(el, event.pointerId);
            send({ type: "selection", op: "anchor", handle: which });
        });
        el.addEventListener("pointermove", (event) => {
            if (!handleDrag || event.pointerId !== handleDrag.id) {
                return;
            }
            event.preventDefault();
            // The knob hangs below its line: aim at the line above the finger.
            queueExtend(event.clientX, event.clientY - 14);
        });
        const end = (event) => {
            if (!handleDrag || event.pointerId !== handleDrag.id) {
                return;
            }
            handleDrag = null;
            release(el, event.pointerId);
            flushExtend();
            const r = el.getBoundingClientRect();
            queueMenu(r.left + 12, r.bottom + MENU_GAP_PX);
        };
        el.addEventListener("pointerup", end);
        el.addEventListener("pointercancel", end);
        surface.appendChild(el);
        return el;
    }

    function queueExtend(clientX, clientY) {
        const p = canvasPoint(clientX, clientY);
        if (!p) {
            return;
        }
        extendPending = p;
        if (!extendTimer) {
            extendTimer = window.setTimeout(() => {
                extendTimer = null;
                flushExtend();
            }, EXTEND_THROTTLE_MS);
        }
    }

    function flushExtend() {
        if (extendTimer) {
            window.clearTimeout(extendTimer);
            extendTimer = null;
        }
        if (extendPending) {
            const p = extendPending;
            extendPending = null;
            send({ type: "selection", op: "extend", x: p.x, y: p.y });
        }
    }

    function placeHandle(el, x, y) {
        el.style.left = Math.round(x) + "px";
        el.style.top = Math.round(y) + "px";
        el.hidden = false;
    }

    /** Handles from a `selection` reply: browser px are canvas px. */
    function showHandles(sel) {
        if (canvas.hidden || !sel || sel.empty || !sel.start || !sel.end) {
            hideHandles();
            if (menuPending) {
                menuPending = false;
                scheduleMenu(menuFallback.x, menuFallback.y);
            }
            return;
        }
        if (!handleStart) {
            handleStart = makeHandle("start");
            handleEnd = makeHandle("end");
        }
        const ox = canvas.offsetLeft;
        const oy = canvas.offsetTop;
        placeHandle(handleStart, ox + sel.start.x, oy + sel.start.y + sel.start.h);
        placeHandle(handleEnd, ox + sel.end.x, oy + sel.end.y + sel.end.h);
        const oneLine = Math.abs(sel.start.y - sel.end.y) < 2;
        selectionBox = {
            left: oneLine ? Math.min(sel.start.x, sel.end.x) : 0,
            right: oneLine ? Math.max(sel.start.x, sel.end.x) : canvas.width,
            top: Math.min(sel.start.y, sel.end.y),
            bottom: Math.max(sel.start.y + sel.start.h, sel.end.y + sel.end.h),
        };
        if (menuPending) {
            menuPending = false;
            const host = surface.getBoundingClientRect();
            // Below the end handle, clear of the text; above the start line when
            // there is no room underneath.
            const below = oy + sel.end.y + sel.end.h + MENU_GAP_PX;
            const y = host.height - below >= 140 ? below : Math.max(0, oy + sel.start.y - MENU_GAP_PX - 140);
            scheduleMenu(host.left + ox + sel.end.x, host.top + y);
        }
    }

    /** Shows the menu once the next `selection` reply has placed the handles, or at the fallback point. */
    function queueMenu(clientX, clientY) {
        menuFallback = { x: clientX, y: clientY };
        menuPending = true;
        window.setTimeout(() => {
            if (menuPending) {
                menuPending = false;
                scheduleMenu(menuFallback.x, menuFallback.y);
            }
        }, 600);
    }

    function scheduleMenu(clientX, clientY) {
        if (menuTimer) {
            window.clearTimeout(menuTimer);
        }
        menuTimer = window.setTimeout(() => {
            menuTimer = null;
            showMenu(clientX, clientY);
        }, MENU_DELAY_MS);
    }

    function hideHandles() {
        if (handleStart) {
            handleStart.hidden = true;
            handleEnd.hidden = true;
        }
    }

    const handlesVisible = () => Boolean(handleStart && !handleStart.hidden);

    function requestSelection() {
        send({ type: "selection", op: "get" });
    }

    /** Asks again for the selection after something that may have moved it. */
    function scheduleSelectionRefresh(delayMs) {
        if (!handlesVisible()) {
            return;
        }
        if (refreshTimer) {
            window.clearTimeout(refreshTimer);
        }
        refreshTimer = window.setTimeout(() => {
            refreshTimer = null;
            requestSelection();
        }, delayMs);
    }

    function pointInSelection(clientX, clientY) {
        if (!selectionBox || !handlesVisible()) {
            return false;
        }
        const p = canvasPoint(clientX, clientY);
        return Boolean(p && p.x >= selectionBox.left && p.x <= selectionBox.right && p.y >= selectionBox.top && p.y <= selectionBox.bottom);
    }

    // ---------------------------------------------------------- context menu

    let menuEl = null;

    function closeMenu() {
        if (menuEl) {
            menuEl.remove();
            menuEl = null;
        }
    }

    /** The right-click and long-press menu: a headless browser's own never reaches the screencast. */
    function showMenu(clientX, clientY) {
        closeMenu();
        const items = [
            ["Copy", () => {
                requestCopy();
                hideHandles();
            }],
            ["Cut", () => {
                requestCopy();
                send({ type: "key", key: "Control+x" });
                hideHandles();
            }],
            ["Paste", () => {
                pasteFromClientClipboard();
                hideHandles();
            }],
            ["Select all", () => {
                send({ type: "key", key: "Control+a" });
                window.setTimeout(requestSelection, 120);
            }],
        ];
        const menu = document.createElement("div");
        menu.className = "wslc-browse-menu";
        menu.setAttribute("role", "menu");
        for (const [label, action] of items) {
            const btn = document.createElement("button");
            btn.type = "button";
            btn.className = "wslc-browse-menu-item";
            btn.setAttribute("role", "menuitem");
            btn.textContent = label;
            // The surface must not see this press: it would take the focus.
            btn.addEventListener("pointerdown", (event) => event.stopPropagation());
            btn.addEventListener("click", (event) => {
                event.stopPropagation();
                closeMenu();
                action();
            });
            menu.appendChild(btn);
        }
        surface.appendChild(menu);
        menuEl = menu;
        const host = surface.getBoundingClientRect();
        const zoom = zoomOf(surface);
        const w = menu.offsetWidth || 160;
        const h = menu.offsetHeight || 120;
        menu.style.left = Math.round(Math.max(0, Math.min(host.width / zoom - w - 2, (clientX - host.left) / zoom))) + "px";
        menu.style.top = Math.round(Math.max(0, Math.min(host.height / zoom - h - 2, (clientY - host.top) / zoom))) + "px";
    }

    listen(document, "pointerdown", (event) => {
        if (menuEl && !menuEl.contains(event.target)) {
            closeMenu();
        }
    });
    listen(document, "keydown", (event) => {
        if (event.key === "Escape" && menuEl) {
            event.preventDefault();
            closeMenu();
        }
    });

    listen(surface, "keydown", onKeyDown);
    listen(ime, "compositionstart", () => {
        imeComposing = true;
    });
    listen(ime, "compositionend", () => {
        imeComposing = false;
        flushIme();
    });
    listen(ime, "input", () => {
        if (!imeComposing) {
            flushIme();
        }
    });
    listen(ime, "paste", (event) => {
        const data = event.clipboardData ? event.clipboardData.getData("text/plain") : "";
        if (data) {
            event.preventDefault();
            send({ type: "key", text: data });
        }
    });

    function flushIme() {
        const text = ime.value;
        if (text) {
            ime.value = "";
            send({ type: "key", text });
            scheduleSelectionRefresh(120);
        }
    }

    // ------------------------------------------------------------ transport

    function socketUrl() {
        const url = new URL(streamUrl);
        url.searchParams.set("viewerId", viewerId());
        return url.toString();
    }

    function connect() {
        if (disposed || (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING))) {
            return;
        }
        if (reconnectTimer) {
            window.clearTimeout(reconnectTimer);
            reconnectTimer = null;
        }
        intentionalClose = false;
        setStatus(reconnectAttempt ? "Reconnecting…" : "Connecting…", "connecting");
        if (!reconnectAttempt) {
            showPlaceholder("Connecting…");
        }
        const sock = new WebSocket(socketUrl());
        sock.binaryType = "arraybuffer";
        ws = sock;
        sock.addEventListener("open", () => {
            whenPaneSized((hole) => {
                if (ws !== sock || sock.readyState !== WebSocket.OPEN) {
                    return;
                }
                viewportW = hole.width;
                viewportH = hole.height;
                const payload = { type: "start", protocol: PROTOCOL, width: viewportW, height: viewportH, viewerId: viewerId() };
                if (sessionId) {
                    payload.sessionId = sessionId;
                }
                send(payload);
            });
        });
        sock.addEventListener("message", (event) => {
            if (event.data instanceof ArrayBuffer) {
                onBinaryMessage(event.data);
                return;
            }
            let msg;
            try {
                msg = JSON.parse(event.data);
            } catch {
                return;
            }
            onMessage(msg || {});
        });
        sock.addEventListener("close", () => {
            if (ws !== sock) {
                return;
            }
            ws = null;
            if (!intentionalClose) {
                scheduleReconnect();
            }
        });
    }

    function onMessage(msg) {
        switch (msg.type) {
            case "ready":
                reconnectAttempt = 0;
                everReady = true;
                setStatus(msg.joined ? "Joined shared session" : msg.resumed ? "Resumed — same page" : "Connected", "ok");
                setAddress(msg.url);
                notify("OnReady", Boolean(msg.owner), Number(msg.zoom) || 100);
                // The hole may have changed while the socket was down.
                syncViewport();
                return;
            case "url":
                setAddress(msg.href);
                return;
            case "ping":
                send({ type: "pong" });
                return;
            case "clipboard":
                writeClientClipboard(String(msg.text || ""));
                return;
            case "focus":
                onFocusReply(msg);
                return;
            case "zoom":
                notify("OnZoom", Number(msg.percent) || 100);
                scheduleSelectionRefresh(200);
                return;
            case "selection":
                showHandles(msg);
                return;
            case "error":
                setStatus(msg.message || "Error", "error");
                showPlaceholder(msg.message || "Error");
                if (!reconnectAttempt && !everReady) {
                    // The agent refused the request itself (the port is not
                    // published, the container is gone): retrying would only
                    // repeat it. Drops after a working session still reconnect.
                    intentionalClose = true;
                }
                return;
            default:
                return;
        }
    }

    function scheduleReconnect() {
        if (reconnectTimer || intentionalClose || disposed) {
            return;
        }
        const delay = backoffMs(reconnectAttempt);
        reconnectAttempt += 1;
        setStatus("Reconnecting…", "connecting");
        reconnectTimer = window.setTimeout(() => {
            reconnectTimer = null;
            connect();
        }, delay);
    }

    /** Leaves the pane; the host browser stays on its page for the next open. */
    function detach() {
        intentionalClose = true;
        if (reconnectTimer) {
            window.clearTimeout(reconnectTimer);
            reconnectTimer = null;
        }
        const sock = ws;
        ws = null;
        if (!sock) {
            return;
        }
        try {
            if (sock.readyState === WebSocket.OPEN) {
                sock.send(JSON.stringify({ type: "detach" }));
            }
            sock.close();
        } catch {
            // Already closing.
        }
    }

    /** The owner only: closes the host browser for everyone attached. */
    function endSession() {
        send({ type: "close" });
        detach();
        setStatus("Session ended", "idle");
        showPlaceholder("Disconnected. Reopen Port to start a new session.");
        notify("OnEnded");
    }

    // ------------------------------------------------------------ lifecycle

    // The hole changes with the window, a rotation, the dialog and the soft keyboard.
    const observer = typeof ResizeObserver === "function" ? new ResizeObserver(scheduleSync) : null;
    if (observer) {
        observer.observe(surface);
    }
    listen(window, "resize", scheduleSync);
    listen(window, "orientationchange", scheduleSync);
    listen(document, "visibilitychange", () => {
        if (document.visibilityState === "visible" && !intentionalClose && !ws) {
            reconnectAttempt = 0;
            connect();
        }
    });
    listen(window, "pagehide", detach);
    listen(window, "beforeunload", detach);

    connect();

    const withSocket = (payload) => () => send(payload);
    return {
        connect,
        detach,
        endSession,
        back: withSocket({ type: "back" }),
        forward: withSocket({ type: "forward" }),
        reload: withSocket({ type: "reload" }),
        navigate: (url) => send({ type: "navigate", url: String(url || "").trim() }),
        zoom: (percent) => send({ type: "zoom", percent }),
        copy: requestCopy,
        paste: pasteFromClientClipboard,
        // A saved login's user or password: to this device's clipboard, or typed into the page's focused field.
        copyText: writeClientClipboard,
        typeText: (text) => {
            if (!send({ type: "key", text: String(text || "") })) {
                setStatus("Not connected", "error");
            }
        },
        dispose() {
            detach();
            disposed = true;
            if (observer) {
                observer.disconnect();
            }
            for (const cleanup of cleanups) {
                cleanup();
            }
            closeMenu();
        },
    };
}
