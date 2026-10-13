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

// A folder of this device, picked to be a build context of the Compose window,
// is held one of two ways, by the picker that gave it. The folder chooser of
// the File System Access API gives the folder itself, to be walked when its
// files are wanted (wslcAgentFolders, by the pick's ticket); the files read
// from it are then parked as they are read. An input that asks for a folder
// gives every file under it at once, which are parked as any pick's are. The
// folder's name is all a browser says of where it is.
const wslcAgentFolders = new Map();

// A file's path from the folder it was picked in, with forward slashes and
// the folder's own name left out: what an input that asks for a folder says.
function wslcAgentInFolder(file) {
    const parts = (file.webkitRelativePath || file.name).split('/');
    return (parts.length > 1 ? parts.slice(1) : parts).join('/');
}

// What a folder picked through an input answers: `count` is how many files it
// holds, since the input has read them all.
function wslcAgentParkFolder(chosen) {
    if (chosen.length === 0) {
        return { ticket: '', name: '', count: 0 };
    }

    const ticket = String(++wslcAgentPicks);
    wslcAgentPicked.set(ticket, chosen);
    return { ticket, name: (chosen[0].webkitRelativePath || '').split('/')[0], count: chosen.length };
}

// What the file of a build context is told of: its place in the ticket, its
// path from the context, its size and the moment it was last written, in seconds.
function wslcAgentListed(file, index, path) {
    return { index, path, size: file.size, modified: Math.floor(file.lastModified / 1000) };
}

const wslcAgentSha256K = new Uint32Array([
    0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
    0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
    0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
    0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
    0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
    0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
    0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
    0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
]);

// One 64-byte block of SHA-256 into the running state.
function wslcAgentSha256Block(state, w, bytes, offset) {
    for (let i = 0; i < 16; i++) {
        const at = offset + 4 * i;
        w[i] = (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];
    }

    for (let i = 16; i < 64; i++) {
        const x = w[i - 15];
        const y = w[i - 2];
        const s0 = ((x >>> 7) | (x << 25)) ^ ((x >>> 18) | (x << 14)) ^ (x >>> 3);
        const s1 = ((y >>> 17) | (y << 15)) ^ ((y >>> 19) | (y << 13)) ^ (y >>> 10);
        w[i] = (w[i - 16] + s0 + w[i - 7] + s1) | 0;
    }

    let a = state[0], b = state[1], c = state[2], d = state[3], e = state[4], f = state[5], g = state[6], h = state[7];
    for (let i = 0; i < 64; i++) {
        const sigma1 = ((e >>> 6) | (e << 26)) ^ ((e >>> 11) | (e << 21)) ^ ((e >>> 25) | (e << 7));
        const t1 = (h + sigma1 + ((e & f) ^ (~e & g)) + wslcAgentSha256K[i] + w[i]) | 0;
        const sigma0 = ((a >>> 2) | (a << 30)) ^ ((a >>> 13) | (a << 19)) ^ ((a >>> 22) | (a << 10));
        const t2 = (sigma0 + ((a & b) ^ (a & c) ^ (b & c))) | 0;
        h = g; g = f; f = e; e = (d + t1) | 0;
        d = c; c = b; b = a; a = (t1 + t2) | 0;
    }

    state[0] += a; state[1] += b; state[2] += c; state[3] += d;
    state[4] += e; state[5] += f; state[6] += g; state[7] += h;
}

// The SHA-256 of a file, in lower-case hex: what the agent knows a file of a
// build context by. A file of a size to hold in memory is handed whole to the
// browser's own digest, where there is one: a page served over plain http
// from another machine has none (crypto.subtle wants a secure context), and a
// file of gigabytes cannot be held. Both of those are read a piece at a time
// and hashed here. `whole` is the size up to which a file is handed whole.
async function wslcAgentSha256(file, whole) {
    const hex = bytes => Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
    if (globalThis.crypto && crypto.subtle && file.size <= whole) {
        return hex(new Uint8Array(await crypto.subtle.digest('SHA-256', await file.arrayBuffer())));
    }

    const state = new Uint32Array([0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19]);
    const w = new Uint32Array(64);
    const last = new Uint8Array(128);
    let kept = 0;
    const piece = 4 * 1024 * 1024;
    for (let at = 0; at < file.size; at += piece) {
        const bytes = new Uint8Array(await file.slice(at, at + piece).arrayBuffer());
        // Every piece but the last is a whole number of blocks; what is left of the last waits for the padding.
        const blocks = bytes.length - (bytes.length % 64);
        for (let offset = 0; offset < blocks; offset += 64) {
            wslcAgentSha256Block(state, w, bytes, offset);
        }

        kept = bytes.length - blocks;
        last.set(bytes.subarray(blocks));
    }

    // The padding: a one bit, zeros, and the length in bits as 64 bits, to a whole block or two.
    last[kept] = 0x80;
    const end = kept < 56 ? 64 : 128;
    last.fill(0, kept + 1, end);
    const view = new DataView(last.buffer);
    view.setUint32(end - 8, Math.floor(file.size / 0x20000000));
    view.setUint32(end - 4, (file.size % 0x20000000) * 8);
    for (let offset = 0; offset < end; offset += 64) {
        wslcAgentSha256Block(state, w, last, offset);
    }

    const digest = new Uint8Array(32);
    state.forEach((word, i) => new DataView(digest.buffer).setUint32(4 * i, word));
    return hex(digest);
}

// The 512 bytes a tar archive says a file with: its name, its size, and the
// sum of the header's own bytes, by which a reader knows it is one. The names
// here are hashes, which fit the hundred characters the plain header has.
function wslcAgentTarHeader(name, size) {
    const header = new Uint8Array(512);
    const put = (text, at) => { for (let i = 0; i < text.length; i++) { header[at + i] = text.charCodeAt(i); } };
    const octal = (value, digits) => value.toString(8).padStart(digits, '0');
    put(name, 0);
    put('0000644', 100);
    put('0000000', 108);
    put('0000000', 116);
    put(octal(size, 11), 124);
    put(octal(Math.floor(Date.now() / 1000), 11), 136);
    put('        ', 148);
    put('0', 156);
    put('ustar', 257);
    put('00', 263);
    put(octal(header.reduce((sum, byte) => sum + byte, 0), 6), 148);
    header[154] = 0;
    header[155] = 32;
    return header;
}

// The UI's own interop, by name: wslcAgent.scrollToEnd (logs' jump to end),
// wslcAgent.stepScroll (a wheel notch a row, the Compose file's tree),
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

    // The projects whose group stands open in the Containers list, as one string
    // ("shop;blog"), kept on this device like the table-or-cards choice. Without
    // a value it only answers the one stored.
    groups(stored) {
        try {
            if (stored !== undefined && stored !== null) {
                localStorage.setItem('wslcAgent.groups', stored);
            }
            return localStorage.getItem('wslcAgent.groups');
        } catch {
            // Private windows: the groups still open and close, it is just not remembered.
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

    // A box whose rows are read one by one (the Compose file's tree): a notch
    // of the mouse wheel moves it one row, the next one brought to the top of
    // the box, instead of the three lines or so a browser moves, which there
    // are several rows at once. Rows are of different heights (a branch may
    // be one line or two), so it goes to the row itself and not a number of
    // pixels. The rows of a branch that stands closed are still in the
    // document, folded away by MudBlazor's collapse: they are not counted.
    // A touchpad sends many small movements and no notches: those are left
    // to the browser, which scrolls them as it always does. A notch is a
    // movement in lines or pages, or one of 30px or more (Chromium sends 100px
    // a notch with Windows at three lines a notch, a third of that at one).
    // Positions are the layout's own (offsetTop), the same measure scrollTop
    // is in, so the app's zoom changes nothing.
    stepScroll(id, rowSelector) {
        const box = document.getElementById(id);
        if (!box || box.dataset.stepScroll) {
            return;
        }

        box.dataset.stepScroll = "true";
        const shown = row => {
            for (let fold = row.parentElement.closest(".mud-collapse-container"); fold && box.contains(fold); fold = fold.parentElement.closest(".mud-collapse-container")) {
                if (!fold.classList.contains("mud-collapse-entered")) {
                    return false;
                }
            }

            return true;
        };
        const topOf = row => {
            let top = 0;
            for (let element = row; element && element !== box; element = element.offsetParent) {
                top += element.offsetTop;
            }

            return top;
        };
        box.addEventListener("wheel", event => {
            const notch = event.deltaMode !== 0 || Math.abs(event.deltaY) >= 30;
            if (!notch || event.deltaY === 0 || event.shiftKey || event.ctrlKey) {
                return;
            }

            // The row at the top stands under the box's own padding, as the first one does.
            const padding = parseFloat(getComputedStyle(box).paddingTop) || 0;
            const tops = [...box.querySelectorAll(rowSelector)].filter(shown).map(row => topOf(row) - padding);
            const next = event.deltaY > 0
                ? tops.find(top => top > box.scrollTop + 1)
                : tops.findLast(top => top < box.scrollTop - 1);
            event.preventDefault();
            box.scrollTop = next ?? (event.deltaY > 0 ? box.scrollHeight : 0);
        }, { passive: false });
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

    // A folder of this device as a build context, for the Compose window: a
    // file read on this device has its build folders here, and what is in them
    // is sent to the agent. In steps, each the browser's own to do, since the
    // files never leave it but to go to the agent: pickFolder opens the folder
    // chooser; pickedText reads one of the folder's files as text (a context's
    // .dockerignore); listPicked says which files a context has; hashPicked
    // says what each is, by the SHA-256 of its bytes, so the agent can say
    // which it lacks; sendPickedFiles sends those. releasePicked lets go of
    // the folder.
    //
    // Two ways of asking for a folder, as there are two of asking for a file.
    // showDirectoryPicker (the File System Access API) says "Select folder",
    // hands over the folder and reads nothing until it is walked, so a folder
    // with a virtual environment in it costs nothing that its .dockerignore
    // leaves out. It needs a secure context (https, or the agent's own
    // machine) and Chromium. Elsewhere an input that asks for a folder, made
    // for the pick: its chooser says "Upload", and reads every file under the
    // folder before it answers. `count` is -1 when the folder has not been read.
    async pickFolder() {
        if (window.showDirectoryPicker) {
            try {
                const folder = await window.showDirectoryPicker({ id: 'wslcAgentContext', mode: 'read' });
                const ticket = String(++wslcAgentPicks);
                wslcAgentFolders.set(ticket, folder);
                wslcAgentPicked.set(ticket, []);
                return { ticket, name: folder.name, count: -1 };
            } catch (failure) {
                if (failure && failure.name === 'AbortError') {
                    // Closed with nothing chosen, which is an answer.
                    return wslcAgentParkFolder([]);
                }
                // A browser that has the API and will not use it here: the
                // input's chooser is still better than no picker at all.
            }
        }

        return new Promise(resolve => {
            const input = document.createElement('input');
            input.type = 'file';
            input.webkitdirectory = true;
            input.multiple = true;
            input.onchange = () => resolve(wslcAgentParkFolder(Array.from(input.files || [])));
            // A chooser closed with nothing chosen: without this the caller
            // would wait for an answer that never comes.
            input.oncancel = () => resolve(wslcAgentParkFolder([]));
            input.click();
        });
    },

    // A file of the folder as text, by its path from the folder; empty when it
    // has none of that name (a context with no .dockerignore).
    async pickedText(ticket, path) {
        const folder = wslcAgentFolders.get(ticket);
        if (!folder) {
            const file = (wslcAgentPicked.get(ticket) || []).find(held => held && wslcAgentInFolder(held) === path);
            return file ? await file.text() : '';
        }

        try {
            const parts = path.split('/');
            const name = parts.pop();
            let within = folder;
            for (const part of parts) {
                within = await within.getDirectoryHandle(part);
            }

            return await (await (await within.getFileHandle(name)).getFile()).text();
        } catch {
            return '';
        }
    },

    // The files of one build context of the folder: those under `under` (a
    // path from the folder, empty for the folder itself), each by its path
    // from there. A folder that was handed over is walked now, and
    // receiver.Enters(path) says of each folder under it whether to go in:
    // what the context's .dockerignore leaves out whole is never read. One
    // whose files were all read at the pick answers the ones under `under`.
    //
    // It answers `files`, and `skipped`: the entries the browser lists and
    // will not hand over (a link whose target is gone, a file the system
    // keeps to itself). They cannot be sent, and one of them stopped the whole
    // context with an error that named nothing; they are left out and named.
    async listPicked(ticket, under, receiver) {
        const held = wslcAgentPicked.get(ticket) || [];
        const folder = wslcAgentFolders.get(ticket);
        if (!folder) {
            const prefix = under ? under + '/' : '';
            const files = [];
            held.forEach((file, index) => {
                const path = file ? wslcAgentInFolder(file) : '';
                if (file && path.startsWith(prefix)) {
                    files.push(wslcAgentListed(file, index, path.slice(prefix.length)));
                }
            });
            return { files, skipped: [] };
        }

        let from = folder;
        try {
            for (const part of under.split('/').filter(Boolean)) {
                from = await from.getDirectoryHandle(part);
            }
        } catch {
            // Not a folder of the one picked: the caller says so, with which one it was.
            return { files: [], skipped: [] };
        }

        const files = [];
        const skipped = [];
        const walk = async (within, path) => {
            let entries;
            try {
                entries = [];
                for await (const entry of within.values()) {
                    entries.push(entry);
                }
            } catch {
                skipped.push((path || '.') + '/');
                return;
            }

            for (const entry of entries) {
                const name = path ? path + '/' + entry.name : entry.name;
                if (entry.kind === 'directory') {
                    if (await receiver.invokeMethodAsync('Enters', name)) {
                        await walk(entry, name);
                    }
                } else {
                    try {
                        const file = await entry.getFile();
                        held.push(file);
                        files.push(wslcAgentListed(file, held.length - 1, name));
                    } catch {
                        skipped.push(name);
                    }
                }
            }
        };
        await walk(from, '');
        return { files, skipped };
    },

    // The full path of a folder of this device, as it was typed for it the
    // last time, by the folder's name: a browser hands over a folder and not
    // where it is, so the path is typed once and remembered on this device.
    folderPath(name) {
        try {
            return (JSON.parse(localStorage.getItem('wslcAgent.folderPaths') || '{}') || {})[name] || '';
        } catch {
            return '';
        }
    },

    rememberFolderPath(name, path) {
        try {
            const kept = JSON.parse(localStorage.getItem('wslcAgent.folderPaths') || '{}') || {};
            kept[name] = path;
            localStorage.setItem('wslcAgent.folderPaths', JSON.stringify(kept));
        } catch {
            // A device that keeps nothing: the path is typed again next time.
        }
    },

    // The hashes of some of a ticket's files, in the order asked for. It takes
    // as long as reading them all does, so it says how far it has got four
    // times a second at most: receiver.Progress(files done, bytes read).
    //
    // A file that cannot be read stops it, by its name (items: index and path
    // each): the browser's own words for it name no file at all.
    async hashPicked(ticket, items, receiver) {
        const files = wslcAgentPicked.get(ticket) || [];
        const hashes = [];
        let bytes = 0;
        let told = 0;
        for (const item of items) {
            const file = files[item.index];
            if (!file) {
                throw new Error('The browser no longer has that folder: pick it again');
            }

            try {
                hashes.push(await wslcAgentSha256(file, 64 * 1024 * 1024));
            } catch (failure) {
                throw new Error('The file ' + item.path + ' could not be read (' + ((failure && failure.name) || 'error') + '). It changed or went while the folder was being read: try again, or leave it out in the .dockerignore');
            }

            bytes += file.size;
            if (Date.now() - told >= 250) {
                told = Date.now();
                await receiver.invokeMethodAsync('Progress', hashes.length, bytes);
            }
        }

        return hashes;
    },

    // Some of a ticket's files to the agent, as one tar archive whose entries
    // are named by their hashes (items: index and hash each). The archive is
    // made of the files themselves and the few bytes between them, which the
    // browser reads from the disk as it sends, so its size costs no memory.
    // By XMLHttpRequest, which says how much has gone, where fetch does not:
    // receiver.Progress(bytes sent, bytes in all).
    sendPickedFiles(ticket, items, url, token, receiver) {
        const files = wslcAgentPicked.get(ticket) || [];
        const parts = [];
        for (const item of items) {
            const file = files[item.index];
            if (!file) {
                return Promise.resolve({ ok: false, status: 0, error: 'The browser no longer has that folder: pick it again' });
            }

            parts.push(wslcAgentTarHeader(item.hash, file.size), file);
            const padding = (512 - (file.size % 512)) % 512;
            if (padding > 0) {
                parts.push(new Uint8Array(padding));
            }
        }

        // Two empty blocks end an archive.
        parts.push(new Uint8Array(1024));
        const body = new FormData();
        body.append('file', new Blob(parts, { type: 'application/x-tar' }), 'files.tar');
        return new Promise(resolve => {
            const request = new XMLHttpRequest();
            let told = 0;
            request.open('POST', url);
            request.withCredentials = true;
            if (token) {
                request.setRequestHeader('Authorization', 'Bearer ' + token);
            }

            request.upload.onprogress = event => {
                if (event.lengthComputable && (Date.now() - told >= 250 || event.loaded === event.total)) {
                    told = Date.now();
                    receiver.invokeMethodAsync('Progress', event.loaded, event.total);
                }
            };
            request.onload = () => {
                if (request.status >= 200 && request.status < 300) {
                    resolve({ ok: true, status: request.status, error: '' });
                    return;
                }

                let problem = null;
                try {
                    problem = JSON.parse(request.responseText);
                } catch {
                    // Not the agent's own answer: a proxy's page, or nothing.
                }

                resolve({ ok: false, status: request.status, error: (problem && (problem.detail || problem.title)) || request.statusText });
            };
            // The agent went, the network went, or a file changed under the browser's feet.
            request.onerror = () => resolve({ ok: false, status: 0, error: 'The files could not be sent: the agent did not answer, or a file of the folder changed while it was being read' });
            request.send(body);
        });
    },

    releasePicked(ticket) {
        wslcAgentPicked.delete(ticket);
        wslcAgentFolders.delete(ticket);
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
