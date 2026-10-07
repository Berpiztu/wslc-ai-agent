// Behaviour MudBlazor lacks, for the shared UI: grid auto-fit (no interop, a
// document listener) and the one named helper the logs view calls.

// The files a pick is holding until each of them has been sent, by the ticket
// pickFiles answered with. Not on the input element they came from: that
// element belongs to a view that closes, and a file waiting its turn in the
// queue must not go with it.
const wslcAgentPicked = new Map();
let wslcAgentPicks = 0;

// What one pick answers, whichever picker answered it. The files are held by a
// ticket of their own, and by position inside it, so that sending does not
// depend on the element or the dialog still being there (the view that owns it
// closes, and a queued file's turn can come long after) and two sends never
// take each other's file. A pick with nothing in it parks nothing: its ticket
// would wait in the page for a send that is never coming, and the page is not
// reloaded for days.
function wslcAgentPark(chosen) {
    if (chosen.length === 0) {
        return { ticket: '', files: [] };
    }

    const ticket = String(++wslcAgentPicks);
    wslcAgentPicked.set(ticket, chosen);
    return { ticket, files: chosen.map((file, index) => ({ index, name: file.name, size: file.size })) };
}

// The UI's own interop, by name: wslcAgent.scrollToEnd (logs' jump to end),
// wslcAgent.pickFiles and wslcAgent.sendFile (the web UI's upload: only the
// browser can send a file of gigabytes without holding it in memory),
// wslcAgent.isHandheld (whether toasts come down at the top centre, as on a phone),
// wslcAgent.clientPlatform (which native client Home offers) and
// wslcAgent.zoom (the page zoom of the title bar's control), wslcAgent.views,
// wslcAgent.logs, wslcAgent.theme and the wslcAgent.dashboard* keys (what this
// device remembers of the lists, the Logs page, the theme and the dashboard).
window.wslcAgent = {
    // The app scaled as a browser's zoom scales it, in the browser and both native
    // clients alike, remembered on this device: --wslc-zoom, which the stylesheet
    // applies to the app, its dialogs, toasts and menu contents (never to the layer
    // MudBlazor positions menus in, which would carry them off screen). Without a
    // percent it only answers the one in use.
    zoom(percent) {
        if (percent) {
            try {
                localStorage.setItem('wslcAgent.zoom', String(percent));
            } catch {
                // Private windows: the zoom still applies, it is just not remembered.
            }
        }
        const current = percent || Number(wslcAgentStoredZoom()) || 100;
        document.documentElement.style.setProperty('--wslc-zoom', String(current / 100));
        return current;
    },

    // The dashboard's own zoom, out of design:
    // --bz-dash-zoom, which the dashboard's stylesheet applies to its canvas
    // alone, on top of the page's; remembered on this device apart from it,
    // one for each page and view ("system/desktop", "user/mobile"…), kept as
    // one JSON object. Without a percent it applies and answers the one kept.
    dashboardZoom(key, percent) {
        let kept = {};
        try {
            const read = JSON.parse(localStorage.getItem('wslcAgent.dashboardZoom') || '{}');
            kept = read && typeof read === 'object' ? read : {};
        } catch {
            kept = {};
        }
        if (percent) {
            kept[key] = percent;
            try {
                localStorage.setItem('wslcAgent.dashboardZoom', JSON.stringify(kept));
            } catch {
                // Private windows: the zoom still applies, it is just not remembered.
            }
        }
        const current = percent || Number(kept[key]) || 100;
        document.documentElement.style.setProperty('--bz-dash-zoom', String(current / 100));
        return current;
    },

    // How the dashboard's view is fitted to a screen of another aspect, out of
    // design — "fit", "fill", "width", "stretch" or "fluid", the first as a television offers them —
    // kept on this device beside the dashboard's
    // zoom, one for each page and view, as one JSON object. Without a mode it
    // only answers the one kept, null where none is: the page chooses then.
    dashboardViewFit(key, mode) {
        let kept = {};
        try {
            const read = JSON.parse(localStorage.getItem('wslcAgent.dashboardViewFit') || '{}');
            kept = read && typeof read === 'object' ? read : {};
        } catch {
            kept = {};
        }
        if (mode) {
            kept[key] = mode;
            try {
                localStorage.setItem('wslcAgent.dashboardViewFit', JSON.stringify(kept));
            } catch {
                // Private windows: the mode still applies, it is just not remembered.
            }
        }
        return mode || kept[key] || null;
    },

    // Table or cards per list, as one string ("Container=cards;ImageSummary=table"),
    // kept on this device: the choice belongs to the client, not to the agent, so
    // closing the app and opening it again finds every list as it was left. Without
    // a value it only answers the one stored.
    views(stored) {
        try {
            if (stored !== undefined && stored !== null) {
                localStorage.setItem('wslcAgent.views', stored);
            }
            return localStorage.getItem('wslcAgent.views');
        } catch {
            // Private windows: the choice still works, it is just not remembered.
            return null;
        }
    },

    // The Logs page as it was left, as one string ("hide=images;level=INFO;
    // follow=1;grid=812"), kept on this device like the table-or-cards choice:
    // coming back to the page finds its toggles, its level, Autorefresh and the
    // divider where it was. Without a value it only answers the one stored.
    logs(stored) {
        try {
            if (stored !== undefined && stored !== null) {
                localStorage.setItem('wslcAgent.logs', stored);
            }
            return localStorage.getItem('wslcAgent.logs');
        } catch {
            // Private windows: the page still works, it is just not remembered.
            return null;
        }
    },

    // The dashboard kept on this device, both its views as one JSON string
    // (DeviceDashboardStore). Without a value it only answers the one stored.
    dashboardV25(stored) {
        return window.wslcAgent.remember('wslcAgent.dashboardV25', stored);
    },

    // The dashboard's designs not saved yet, every place's, page's and view's
    // in one JSON string (DeviceDashboardDrafts): nothing reaches where the
    // dashboard is kept without Save, and this keeps the design through a
    // power cut or a lost connection until then.
    dashboardV25Draft(stored) {
        return window.wslcAgent.remember('wslcAgent.dashboardV25Draft', stored);
    },

    // Where this device keeps the dashboard: "device", or "user", the agent,
    // so the phone and the desktop open the same one (DashboardPlace).
    dashboardV2Scope(stored) {
        return window.wslcAgent.remember('wslcAgent.dashboardV2Scope', stored);
    },

    // Which of the dashboard's pages this device looked at last, System or
    // User, opened again the next time.
    dashboardV2Page(stored) {
        return window.wslcAgent.remember('wslcAgent.dashboardV2Page', stored);
    },

    // What this device remembers under a key: written when a value is given,
    // and the one stored answered. Private windows remember nothing, and the
    // application works as a device that never chose.
    remember(key, stored) {
        try {
            if (stored !== undefined && stored !== null) {
                localStorage.setItem(key, stored);
            }
            return localStorage.getItem(key);
        } catch {
            return null;
        }
    },

    // Light or dark, as this device last had it: the theme belongs to the client,
    // not to the agent, so two clients of the same agent each keep their own and
    // the app opens in the theme it was left in. Without an argument it only
    // answers the one stored.
    theme(dark) {
        try {
            if (dark !== undefined && dark !== null) {
                localStorage.setItem('wslcAgent.theme', dark ? 'dark' : 'light');
            }

            return localStorage.getItem('wslcAgent.theme') === 'dark';
        } catch {
            // Private windows: the theme still turns over, it is just not remembered.
            return false;
        }
    },

    // The look chosen in Settings (Layout), as this device keeps it: the same
    // kind of setting as the theme and the page zoom, so it belongs to the
    // client and not to the agent. Without an argument it only answers what is
    // stored.
    style(json) {
        try {
            if (json !== undefined && json !== null) {
                localStorage.setItem('wslcAgent.style', json);
            }

            return localStorage.getItem('wslcAgent.style');
        } catch {
            // Private windows: the look still applies, it is just not remembered.
            return null;
        }
    },

    // A file the application writes out of the browser: a look, a dashboard
    // exported, so it can be carried to another machine. Asked for the way
    // pickFiles asks for one to send, for the same reason: Chromium's own
    // chooser, the one a download opens, stops answering — Save is pressed
    // again and again and nothing is written (the owner, 1 October 2026, as
    // Open did before). showSaveFilePicker does not go through it, and opens
    // where it was left. The download stays for a page that is not a secure
    // context, for Firefox, and for anything the API refuses.
    async download(name, text, type) {
        const blob = new Blob([text], { type: type || 'application/json' });
        if (window.showSaveFilePicker) {
            try {
                const handle = await window.showSaveFilePicker({ suggestedName: name, id: 'wslcAgentSave' });
                const writable = await handle.createWritable();
                await writable.write(blob);
                await writable.close();
                return;
            } catch (failure) {
                if (failure && failure.name === 'AbortError') {
                    // Closed with nothing chosen, which is an answer.
                    return;
                }
                // A browser that has the API and will not use it here (a page
                // the user has not touched for a while): the download instead.
            }
        }

        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = name;
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    },

    // The look the application ships with: a file beside this one, read once at
    // startup. It is what the product looks like, so it is asked for before the
    // device's own choices and overridden by them.
    async styleFile() {
        try {
            const response = await fetch('/_content/WslcAgent.UI/wslc-style.json', { cache: 'no-cache' });
            return response.ok ? await response.text() : null;
        } catch {
            // Offline or missing: the values compiled in are the same ones.
            return null;
        }
    },

    // A look applied but not saved, kept for this tab alone: reloading the page
    // finds it still on, closing the application forgets it and what was saved
    // comes back. An empty string clears it, which is what saving does.
    styleTried(json) {
        try {
            if (json !== undefined && json !== null) {
                if (json === '') {
                    sessionStorage.removeItem('wslcAgent.styleTried');
                } else {
                    sessionStorage.setItem('wslcAgent.styleTried', json);
                }
            }

            return sessionStorage.getItem('wslcAgent.styleTried');
        } catch {
            return null;
        }
    },

    // What is not a length: a name written on the document, for the rules that
    // cannot be a number — how a button is filled, for one.
    styleFlags(flags) {
        for (const [name, value] of Object.entries(flags || {})) {
            document.documentElement.setAttribute(name, value);
        }
    },

    // What the stylesheet reads: one custom property per value, on the document,
    // so applying a look is one write and no screen has to be drawn again.
    styleVars(values) {
        for (const [name, value] of Object.entries(values || {})) {
            if (value === '') {
                // Nothing chosen for it: the theme's own value comes back.
                document.documentElement.style.removeProperty(name);
            } else {
                document.documentElement.style.setProperty(name, value);
            }
        }
    },
    // With `follow`, a box the reader has scrolled up in is left where it is,
    // as a pull console does; scrolling back down resumes it.
    // (The Logs page does not use it: it holds its list while the reader is on
    // it, and otherwise goes to the end every time.)
    // A grid is scrolled by the table MudBlazor puts inside it, so the box
    // named is a grid's wrapper as often as the scrolling box itself.
    scrollToEnd(id, follow = false) {
        const named = document.getElementById(id);
        if (!named) {
            return;
        }

        const element = named.querySelector(".mud-table-container") ?? named;

        if (follow && !element.dataset.followWatched) {
            element.dataset.followWatched = "true";
            element.dataset.atEnd = "true";
            element.addEventListener("scroll", () => {
                element.dataset.atEnd = String(element.scrollHeight - element.scrollTop - element.clientHeight < 40);
            });
        }

        if (!follow || element.dataset.atEnd === "true") {
            // Once now, and again on the next two frames: a box of panels settles
            // its height a frame or two after Blazor has drawn it, and a single
            // scroll measured mid-layout landed a panel short of the end.
            const toEnd = () => { element.scrollTop = element.scrollHeight; };
            toEnd();
            requestAnimationFrame(() => { toEnd(); requestAnimationFrame(toEnd); });
        }
    },

    // Files into a container, chosen and sent by the browser itself, in two
    // steps so the view can say what is going while it goes: pickFiles opens
    // the rail's own input and answers what was chosen, sendFile posts one of
    // them. Several chosen at once are several sends, and the agent keeps one
    // job for each.
    //
    // Why the browser and not .NET: in WebAssembly a request cannot be
    // streamed — the browser only takes a streaming body over HTTP/2 with
    // duplex, and the agent serves HTTP/1.1 — so .NET would have to hold the
    // whole file in memory, twice, before the first byte left. A backup of a
    // container is gigabytes. Handed to fetch, the browser reads it from the
    // disk as it sends it and costs nothing, whatever the size.
    //
    // No progress here: the agent counts what it receives, and the ring on the
    // container's row is that count (GET /containers/uploads).
    // Two ways of asking for a file, because Chromium has two and only one of
    // them is reliable here. showOpenFilePicker (the File System Access API)
    // does not go through the element at all; the element's own chooser is
    // what stops answering — it takes a selection and does nothing with Open,
    // again and again, until the page is reloaded (seen on Windows 11, with
    // the picker alive and waiting on our side: the
    // dialog answered Escape and refused Open). The API also opens where it
    // was left, per id, which the element's chooser cannot be told.
    //
    // It needs a secure context, which the agent's own machine is (localhost)
    // and a WebView is, but a plain http page on another machine is not, and
    // Firefox has neither the API nor a plan for one. The input stays for all
    // of those, and for anything the API refuses.
    async pickFiles(input) {
        if (window.showOpenFilePicker) {
            try {
                const handles = await window.showOpenFilePicker({ multiple: true, id: 'wslcAgentUpload' });
                return wslcAgentPark(await Promise.all(handles.map(handle => handle.getFile())));
            } catch (failure) {
                if (failure && failure.name === 'AbortError') {
                    // Closed with nothing chosen, which is an answer.
                    return wslcAgentPark([]);
                }
                // A browser that has the API and will not use it here: the
                // element's chooser is still better than no picker at all.
            }
        }

        return new Promise(resolve => {
            // The same file twice in a row still raises change: the value is
            // cleared before the picker opens, not after it answers.
            input.value = '';
            const done = chosen => {
                input.onchange = null;
                input.oncancel = null;
                resolve(wslcAgentPark(chosen));
            };
            input.onchange = () => done(Array.from(input.files || []));
            // A picker closed with nothing chosen: without this the caller
            // would wait for an answer that never comes.
            input.oncancel = () => done([]);
            input.click();
        });
    },

    // One of the files a ticket is holding, to the agent. The request lives in
    // the page, not in the screen that asked for it: closing the Files view
    // does not touch it. The answer is the agent's, whole, so the caller can
    // show what it said when it refused.
    async sendFile(ticket, index, url, token) {
        const files = wslcAgentPicked.get(ticket) || [];
        const file = files[index];
        // Its place is emptied, not removed: the others keep their own. The
        // ticket goes when the last of its files has gone.
        files[index] = null;
        if (files.every(waiting => !waiting)) {
            wslcAgentPicked.delete(ticket);
        }

        if (!file) {
            return { ok: false, status: 0, error: 'The browser no longer has that file to send' };
        }

        const body = new FormData();
        body.append('file', file, file.name);
        try {
            const response = await fetch(url, {
                method: 'POST',
                body,
                credentials: 'include',
                headers: token ? { Authorization: 'Bearer ' + token } : {},
            });
            if (response.ok) {
                return { ok: true, status: response.status, error: '' };
            }

            const problem = await response.json().catch(() => null);
            return { ok: false, status: response.status, error: (problem && (problem.detail || problem.title)) || response.statusText };
        } catch (failure) {
            // The agent went, the network went, or the request was aborted.
            return { ok: false, status: 0, error: String((failure && failure.message) || failure) };
        }
    },

    // A finger is the primary pointer: a phone or a tablet, where the thumb
    // reaches the right edge sooner than the left. A mouse, however small the
    // window, is not.
    isHandheld() {
        return window.matchMedia('(pointer: coarse)').matches;
    },

    // Tells .NET each time the page goes out of sight or comes back into it: a
    // phone's app put in the background and opened again, a tab left and found.
    watchVisibility(receiver) {
        document.addEventListener('visibilitychange', () => {
            receiver.invokeMethodAsync('VisibilityChanged', document.visibilityState === 'visible');
        });
    },

    // How wide a text is in ems, in the font and letter spacing of the element
    // it will be shown in: what lets a field size its text to fit exactly,
    // instead of assuming every letter as wide as the widest. Measured once
    // the web fonts are in, so a fallback font's widths are never the answer.
    async textEms(element, text) {
        await document.fonts.ready;
        const style = getComputedStyle(element);
        const context = document.createElement('canvas').getContext('2d');
        context.font = `${style.fontStyle} ${style.fontWeight} 100px ${style.fontFamily}`;
        const spacing = (parseFloat(style.letterSpacing) || 0) / parseFloat(style.fontSize);
        return context.measureText(text).width / 100 + spacing * text.length;
    },

    // Back, in the order a native client walks it: whatever is over the
    // page first, then the navigation that opened over it, and only then the
    // step before. False when there is nothing left, which is what lets the
    // native client leave the app instead of swallowing the gesture.
    //
    // The native client asks for this because its web view has no history of
    // its own to walk: the UI navigates inside one document, so its back stack
    // lives here, not in the view.
    back() {
        if (this.leaveDialog()) {
            return true;
        }

        // The navigation drawer of a narrow window is an overlay over the page,
        // and its overlay is what closes it.
        const overlay = document.querySelector('.mud-overlay-drawer, .mud-drawer-overlay, .mud-overlay');
        if (overlay) {
            overlay.click();
            return true;
        }

        if (this.historyIndex() > 0) {
            window.history.back();
            return true;
        }

        return false;
    },

    // Leaves the dialog on top, the last opened, by its own way out: the button
    // its title row carries, so one that holds unsaved work asks before it
    // goes, as that button does. Not Escape: MudBlazor hears it only on the
    // dialog, only where the dialog lets it close, and a dialog holding a
    // terminal or a form keeps the key for itself. Close before Cancel: a
    // pull's console has both, and its Cancel stops the pull. False when no
    // dialog is open. The browser's own back comes here too (BackLeavesDialogs).
    leaveDialog() {
        const dialogs = document.querySelectorAll('.mud-dialog-container .mud-dialog');
        if (dialogs.length === 0) {
            return false;
        }

        const top = dialogs[dialogs.length - 1];
        const buttons = [...top.querySelectorAll('.wslc-dialog-head button, .mud-dialog-actions button')]
            .filter(button => !button.disabled);
        const leave = ['Close', 'Done', 'Cancel', 'No']
            .map(word => buttons.find(button => button.textContent.trim() === word))
            .find(Boolean)
            ?? top.querySelector('.mud-button-close');
        leave?.click();
        return true;
    },

    // Where the browser stands in the app's own history: Blazor numbers its
    // entries in history.state (_index, none on the first). history.length is
    // no measure: it keeps counting the entries already gone back over, so on
    // the first screen it still said there was somewhere to go, and the
    // gesture never left the app.
    historyIndex() {
        return window.history.state?._index ?? 0;
    },

    // Which native client this browser's device takes: "android", "windows"
    // or "ios" (none yet). Client hints first, since a desktop-site Chrome on
    // a phone spoofs a desktop user agent; iPadOS in desktop mode looks like a
    // Mac and must not be offered the Windows installer.
    clientPlatform() {
        if (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1) {
            return 'ios';
        }
        const hints = navigator.userAgentData;
        if (hints) {
            const hinted = String(hints.platform || '').toLowerCase();
            if (hinted === 'android' || hinted === 'ios') {
                return hinted;
            }
            if (hints.mobile && hinted !== 'windows' && hinted !== 'macos') {
                return 'android';
            }
        }
        const ua = navigator.userAgent || '';
        if (/Android/i.test(ua)) {
            return 'android';
        }
        if (/iPhone|iPad|iPod/i.test(ua)) {
            return 'ios';
        }
        const platform = String(navigator.platform || '');
        if (/linux/i.test(platform) && (/arm|aarch64/i.test(platform) || navigator.maxTouchPoints > 1)) {
            return 'android';
        }
        return 'windows';
    },
};

// A page's verb bar (the row under the title bar) never wraps and never loses a
// verb. When the room runs short it gives things up one at a time, in this
// order: the texts of the stats at the left first, then the words on
// the filter switches, then the size of the tools, and only at the very end the
// words on the buttons — a verb that cannot be read is the last thing to lose.
//
// Every bar measures what it needs against the room it was given, so this is
// not a phone-or-desktop switch: a page with one verb keeps everything at a
// width where a page with six cannot, and the same page keeps it in a wide
// window and gives it up in a narrow one. CSS cannot ask whether its content
// fits, which is why the measuring is here and the steps are there (the
// .wslc-fit-* rules of wslc-agent-ui.css).
// The texts a bar gives up when the room runs short, in order: the
// stats at the left first, then the words on the filter switches (their initial
// and their tooltip stay), and the words on the buttons last of all.
const wslcFitSteps = {
    // The page's verb bar: the stats at the left, then the switches' words.
    '.wslc-toolbar': ['small', 'tight', 'letters', 'icons'],
    // The title bar: the search box gives up width first — it is a box, not a
    // word — then the application's name over the session line (the session
    // line stays: it is what the bar is for), then the words on the buttons.
    // The zoom stays at every width.
    '.wslc-appbar-row': ['search', 'brand', 'icons'],
    // The navigation button's row of sections:
    // the air between the buttons goes first, then the words drop to the
    // theme's smallest size, then the row closes up, and only when that is not
    // enough the words go and the glyphs stay.
    '.wslc-verbs-nav': ['tight', 'small', 'icons'],
};

const wslcFitKinds = Object.keys(wslcFitSteps);

// The title of the title bar asks for its whole name — the page's title was
// reading "Contai…" while the search box beside it kept 14rem — but never for
// more than this, so that a container called after its whole registry path does
// not drag the bar down a step on its own. Past that it ends in an ellipsis, as
// it always has. The brand block beside it is counted whole.
const wslcFitGives = '.wslc-appbar-start';
const wslcFitGivesTitle = '.wslc-title';
const wslcFitGivesMost = 14 * 16;

// Between the switches' words and the buttons' words the bar closes up instead:
// it shrinks by exactly what is missing, never past this much of its size. Five
// pixels short is not a reason to take a verb's word away.
const wslcFitFloor = 0.8;

// The steps that make a text smaller and take nothing away.
const wslcFitShrinks = new Set(['small']);

const wslcFitObserved = new WeakSet();
let wslcFitQueued = false;

// The steps this kind of bar has to give, in order.
function wslcStepsOf(bar) {
    const kind = wslcFitKinds.find(one => bar.matches(one));
    return kind ? wslcFitSteps[kind] : [];
}

// The bar as it stands after the first `taken` steps.
function wslcFitStep(bar, taken) {
    wslcStepsOf(bar).forEach((step, index) => bar.classList.toggle('wslc-fit-' + step, index < taken));
}

// 1 is the bar as it stands on a monitor.
function wslcFitScale(bar, scale) {
    bar.style.setProperty('--wslc-fit', String(scale));
}

// What the bar would need with nothing squeezed: each child measured at its own
// natural width, one at a time, while the bar keeps the width it has. The bar
// is a container-query container, so widening it to measure would answer with
// the type sizes of a bar that wide.
function wslcBarNeeds(bar) {
    const style = getComputedStyle(bar);
    const gap = parseFloat(style.columnGap) || 0;
    let need = 0;
    let shown = 0;
    for (const child of bar.children) {
        if (getComputedStyle(child).display === 'none') {
            continue;
        }

        child.classList.add('wslc-measure');
        // Layout pixels, as the room is: the app is zoomed as a browser zooms
        // it, and a bounding rectangle answers in screen pixels — at 150 % a
        // row measured that way seemed half again as wide as its room and the
        // steps came out wrong.
        const natural = child.offsetWidth;
        child.classList.remove('wslc-measure');
        if (child.matches(wslcFitGives)) {
            const title = child.querySelector(wslcFitGivesTitle);
            const titleWidth = title ? title.offsetWidth : 0;
            need += natural - titleWidth + Math.min(titleWidth, wslcFitGivesMost);
        } else {
            need += natural;
        }
        shown += 1;
    }

    return need + Math.max(0, shown - 1) * gap;
}

// The gaps between the bar's children: the bar's own, which the zoom on the
// children does not make smaller.
function wslcBarGaps(bar) {
    const shown = [...bar.children].filter(child => getComputedStyle(child).display !== 'none').length;
    return Math.max(0, shown - 1) * (parseFloat(getComputedStyle(bar).columnGap) || 0);
}

// How much of its size the bar keeps to fit its room, never below the floor.
// Only the children are zoomed, so only what the gaps leave is shared out:
// divided whole, the gaps included, the bar came out a few pixels short every
// time and a text went that a closer bar would have kept.
function wslcCloseUp(bar, room) {
    const gaps = wslcBarGaps(bar);
    // What the bar draws as well as what its children measure: a pixel or two
    // the measure does not see (a rounding, a line under a line) left the bar
    // over by that much with a scale above 1, which changes nothing.
    const style = getComputedStyle(bar);
    const drawn = bar.scrollWidth - (parseFloat(style.paddingLeft) || 0) - (parseFloat(style.paddingRight) || 0);
    const need = Math.max(wslcBarNeeds(bar), drawn);
    return Math.min(1, Math.max(wslcFitFloor, (room - gaps) / Math.max(1, need - gaps)));
}

// Both the sum and the bar itself have to agree: the sum says what the children
// need, the bar says what they took.
function wslcBarFits(bar, room) {
    return wslcBarNeeds(bar) <= room + 1 && bar.scrollWidth <= bar.clientWidth + 1;
}

// A bar is measured again only when its room or what it holds has changed.
// Each pass takes the bar back to its full size to measure it, and for that
// moment a list under it is shorter: every refresh of the Logs page moved both
// lists' scroll 3 px and back, over and over.
function wslcFitBar(bar) {
    const style = getComputedStyle(bar);
    const room = bar.clientWidth - (parseFloat(style.paddingLeft) || 0) - (parseFloat(style.paddingRight) || 0);
    const key = `${room}|${wslcFitText(bar)}|${bar.getElementsByTagName('*').length}`;
    if (bar.dataset.fitKey === key) {
        return;
    }

    // The bar keeps its height while it is measured: taken back to its full
    // size for a moment it grew a pixel or two, the pane under it shrank, and
    // a list at its end had its scroll cut and was sent back down by its
    // Autorefresh — with both of the Logs page's on, every entry that changed
    // the log's count moved both lists.
    const height = bar.style.height;
    bar.style.height = `${bar.offsetHeight}px`;
    try {
        wslcFitBarAt(bar, room);
    } finally {
        bar.style.height = height;
    }

    bar.dataset.fitKey = key;
}

// What a bar shows, for the key it is measured again by. Once its stats' texts
// are gone (tight) theirs is left out: the log's count changes with every
// entry, and each change had the bar show it to measure it, find it did not
// fit and hide it again — the log moved under the reader while Activity,
// whose count stands still at its 200, did not. A change of room still measures it again, and gives the texts back
// where they fit.
function wslcFitText(bar) {
    const stats = bar.classList.contains('wslc-fit-tight') ? bar.querySelector(':scope > .wslc-stats') : null;
    return stats ? bar.textContent.replace(stats.textContent, '') : bar.textContent;
}

function wslcFitBarAt(bar, room) {
    if (room <= 0) {
        // Not on screen (a tab that is not the open one): measured now it would
        // answer zero, and a bar nobody is looking at would be stripped.
        return;
    }

    // Every pass starts from the top — the bar whole, at the size it has on a
    // monitor — and comes down from there. A phone turned on its side has room
    // again, and what it gave up when it was upright it takes straight back.
    for (let taken = 0; taken < wslcStepsOf(bar).length; taken += 1) {
        wslcFitScale(bar, 1);
        wslcFitStep(bar, taken);
        if (wslcBarFits(bar, room)) {
            return;
        }

        if (taken + 1 === wslcStepsOf(bar).length) {
            // The next step would take the words off the buttons. Before that,
            // the bar closes up by what is missing and no more.
            break;
        }

        // A step that only makes a text smaller is tried with the bar closed up
        // too, before the next step takes a text away: a smaller word is still
        // read, one that is gone is not.
        if (taken > 0 && wslcFitShrinks.has(wslcStepsOf(bar)[taken - 1])) {
            wslcFitScale(bar, wslcCloseUp(bar, room));
            if (wslcBarFits(bar, room)) {
                return;
            }
        }
    }

    if (wslcBarFits(bar, room)) {
        return;
    }

    wslcFitScale(bar, wslcCloseUp(bar, room));
    if (wslcBarFits(bar, room)) {
        return;
    }

    // Not even closed up to the floor: the words go, back at full size, and the
    // bar closes up again only if it still has to.
    wslcFitScale(bar, 1);
    wslcFitStep(bar, wslcStepsOf(bar).length);
    if (!wslcBarFits(bar, room)) {
        wslcFitScale(bar, wslcCloseUp(bar, room));
    }
}

// The page's verbs over the navigation button stand one over the other and
// grow upwards, and on a short window the top ones went past its edge. The
// column measures itself against the window's height as a bar does against its
// width, and gives up in this order:
// the air between the verbs, then their size — MudBlazor's small FAB and the
// theme's smallest words — and at the last it closes up by what is missing.
// A verb keeps its word: it is what says what it does.
const wslcVerbsSteps = ['tight', 'small'];

// How close to the window's top the highest verb may stand.
const wslcVerbsMargin = 8;

// Closed up no further than this: past it a word is no longer read.
const wslcVerbsFloor = 0.5;

function wslcVerbsFit(menu) {
    const key = `${window.innerHeight}|${menu.getElementsByTagName('*').length}`;
    if (menu.dataset.fitKey === key) {
        return;
    }

    const over = () => wslcVerbsMargin - menu.getBoundingClientRect().top;
    menu.style.setProperty('--wslc-fit-verbs', '1');
    for (let taken = 0; taken <= wslcVerbsSteps.length; taken += 1) {
        wslcVerbsSteps.forEach((step, index) => menu.classList.toggle('wslc-fit-' + step, index < taken));
        if (over() <= 0) {
            menu.dataset.fitKey = key;
            return;
        }
    }

    // Still past the top at the smallest step: the verbs close up by what
    // they go past it, the row of sections under them left as it is.
    const lists = [...menu.querySelectorAll(':scope > .wslc-verbs-list')];
    const tall = lists.reduce((sum, list) => sum + list.getBoundingClientRect().height, 0);
    if (tall > 0) {
        menu.style.setProperty('--wslc-fit-verbs', String(Math.max(wslcVerbsFloor, (tall - over()) / tall)));
    }

    menu.dataset.fitKey = key;
}

// A list keeps its identity columns frozen at the left and its actions at the
// right, so neither leaves the screen while the row is read sideways. On a
// phone that block can take nearly the whole width — the checkbox, the state,
// the name and the actions left 93 px of a 395 px screen to scroll through
// nine columns — and a table that cannot be read sideways is not a table. So
// when the frozen block takes more than this much of the width, the name is let
// go: the checkbox and the state stay at the left, the actions stay at the
// right, and everything else scrolls under them.
const wslcFrozenMost = 0.6;

function wslcFreezeFit(grid) {
    const head = grid.querySelector('thead tr');
    const room = grid.clientWidth;
    if (!head || room <= 0) {
        // Between two paints a list can measure nothing at all. Letting the
        // columns go at that moment and leaving them there is how a table ended
        // up with four frozen columns out of eight: what it had stays until
        // there is something to measure.
        return;
    }

    // Measured as it stands whole, so a list that gets its room back — a phone
    // turned on its side, a window widened — freezes the name again.
    grid.classList.remove('wslc-thaw');

    let frozen = 0;
    for (const cell of head.children) {
        if (cell.classList.contains('sticky-left') || cell.classList.contains('sticky-right')) {
            frozen += cell.getBoundingClientRect().width;
        }
    }

    if (frozen > room * wslcFrozenMost) {
        grid.classList.add('wslc-thaw');
    }
}

function wslcFitBars() {
    wslcFitQueued = false;
    for (const bar of document.querySelectorAll(wslcFitKinds.join(', '))) {
        if (!wslcFitObserved.has(bar)) {
            wslcFitObserved.add(bar);
            wslcFitRoom.observe(bar);
        }

        wslcFitBar(bar);
    }

    for (const menu of document.querySelectorAll('.wslc-verbs-menu')) {
        wslcVerbsFit(menu);
    }

    for (const grid of document.querySelectorAll('.wslc-fill')) {
        if (!wslcFitObserved.has(grid)) {
            wslcFitObserved.add(grid);
            wslcFitRoom.observe(grid);
        }

        wslcFreezeFit(grid);
        wslcTableFloor(grid);
    }
}

// The narrowest a table may be: every column as it stands and the one that
// takes the spare width at its declared minimum, which a fixed layout does not
// read on its own — on a phone that column was left 0 px wide. Written on the
// list's box, where Blazor writes no style, for the stylesheet to use.
function wslcTableFloor(grid) {
    const head = grid.querySelector(':scope > .mud-table table')?.tHead;
    if (!head || grid.clientWidth <= 0) {
        return;
    }

    const cells = [...head.rows].reduce((most, row) => (row.cells.length > most.length ? [...row.cells] : most), []);
    const floor = cells.reduce((sum, cell) => sum + (cell.classList.contains('wslc-col-stretch')
        ? parseFloat(getComputedStyle(cell).minWidth) || 0
        : cell.getBoundingClientRect().width), 0);
    grid.style.setProperty('--wslc-table-min', `${Math.ceil(floor)}px`);
}

function wslcFitSoon() {
    if (!wslcFitQueued) {
        wslcFitQueued = true;
        requestAnimationFrame(wslcFitBars);
    }
}

// The room changes (the window, the sidebar, a phone turning over) and so does
// what the bar holds (a value grows a digit, a verb appears with a selection):
// both send the bars back to the tape measure. Attribute changes are left out
// on purpose — the step is written as one, and watching it would chase its own
// tail.
const wslcFitRoom = new ResizeObserver(wslcFitSoon);
// The verbs' column is measured against the window's height, which no
// element's size reports.
window.addEventListener('resize', wslcFitSoon);
// A table's rows are not a bar's business: they change on every refresh and
// fit nothing (the header row, which a grid's floor is measured from, is not
// in the body).
new MutationObserver(changes => {
    if (changes.some(change => !(change.target instanceof Element ? change.target : change.target.parentElement)?.closest('tbody'))) {
        wslcFitSoon();
    }
}).observe(document.documentElement, {
    childList: true,
    subtree: true,
    characterData: true,
});
wslcFitSoon();

// Double-click a column's resize handle and the column goes back to the width
// the application gives it: dropping the width the drag wrote inline leaves the
// stylesheet's own, which is the default every table starts at.
document.addEventListener('dblclick', event => {
    const resizer = event.target.closest('.wslc-fill .mud-resizer');
    if (resizer) {
        resizer.closest('th').style.width = '';
    }
});

// A press on a column's resize handle is a resize and never the start of the
// header's drag to reorder the columns. The browser began that drag the moment
// the handle was pressed, cancelled the pointer (pointercancel) and took the
// resize's capture with it, so no column could be made wider or narrower —
// the drag moved it instead (measured on the Logs page: dragstart 1 ms after the press, the capture lost 5 ms later).
// A drag from anywhere else on the header still reorders.
// An opened row's detail is one cell MudBlazor spans over "1000" columns. In
// the fixed layout the tables use, those are a thousand phantom columns that
// share the spare width with the one column meant to hold it — the log's
// Message, Activity's Command — and leave it one pixel wide the moment a row
// opens (measured: Message 869 px closed, 1 px
// open). The cell spans the columns the table has, once a frame at most.
let wslcSpanQueued = false;
function wslcSpanDetails() {
    wslcSpanQueued = false;
    for (const cell of document.querySelectorAll('.wslc-fill td.mud-table-child-content[colspan="1000"]')) {
        const head = cell.closest('table')?.tHead;
        const columns = head ? Math.max(0, ...[...head.rows].map(row => row.cells.length)) : 0;
        if (columns > 0) {
            cell.colSpan = columns;
        }
    }
}

new MutationObserver(() => {
    if (!wslcSpanQueued) {
        wslcSpanQueued = true;
        requestAnimationFrame(wslcSpanDetails);
    }
}).observe(document.documentElement, { childList: true, subtree: true });

let wslcResizing = false;
document.addEventListener('pointerdown', event => {
    wslcResizing = event.target instanceof Element && event.target.closest('.wslc-fill .mud-resizer') !== null;
}, true);
document.addEventListener('dragstart', event => {
    if (wslcResizing) {
        event.preventDefault();
    }
}, true);
for (const end of ['pointerup', 'pointercancel']) {
    document.addEventListener(end, () => {
        if (wslcResizing) {
            // A column changed width: the table's floor with it.
            wslcFitSoon();
        }

        wslcResizing = false;
    }, true);
}

// The page itself never scrolls — each screen scrolls inside its own column —
// but a phone's keyboard can leave it scrolled: the browser lifts the field
// being typed in above the keyboard, and when the keyboard goes the page stays
// lifted, the screen shifted and the keyboard's space empty under it. When the
// visible area grows back, the page is put back at its top.
if (window.visualViewport) {
    let wslcViewHeight = window.visualViewport.height;
    window.visualViewport.addEventListener('resize', () => {
        const height = window.visualViewport.height;
        if (height > wslcViewHeight && document.scrollingElement && document.scrollingElement.scrollTop !== 0) {
            document.scrollingElement.scrollTop = 0;
        }

        wslcViewHeight = height;
    });
}

// The zoom this device chose, read before the first paint so the page does not
// jump from 100 % to it once Blazor starts.
function wslcAgentStoredZoom() {
    try {
        return localStorage.getItem('wslcAgent.zoom');
    } catch {
        return null;
    }
}

window.wslcAgent.zoom();
