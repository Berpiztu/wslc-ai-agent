// The terminal surface: xterm.js plus the agent's exec protocol
// (ExecTerminals). Loaded as a module the first time a terminal opens, so the
// 500 KB emulator costs nothing on the pages that have no terminal.
//
//   client → {type:"start",command,cols,rows} {type:"stdin",data}
//            {type:"resize",cols,rows} {type:"pong"} {type:"close"}
//   agent  → {type:"ready",backend,pty} {type:"stdout",data} {type:"ping"}
//            {type:"warning",message} {type:"exit",code} {type:"error",message}
//
// With a pty the keystrokes go straight through and the shell echoes. Behind a
// pipe nothing echoes, so a small line editor draws what is typed and sends
// whole lines. Ctrl+Shift+C copies the selection; Ctrl+C copies only when
// something is selected and otherwise reaches the shell as SIGINT; paste stays
// with the browser (Ctrl+V, Shift+Insert, right-click) so it never doubles.
//
// A session outlives the component that opened it. Leaving the page detaches
// the surface and keeps the socket, so the shell on the agent and everything
// it printed are still there when the page is opened again; the component
// finds its session by key (the host shell, a job, one container's exec) and
// takes the same surface back. Only Close, Log out and the tab closing end a
// shell. The registry is this tab's: another device or tab has its own.

const VENDOR = "_content/WslcAgent.UI/vendor/xterm";

const THEME = {
    background: "#0b1220",
    foreground: "#e2e8f0",
    cursor: "#e2e8f0",
    cursorAccent: "#0b1220",
    selectionBackground: "rgba(148, 163, 184, 0.35)",
};

let emulator = null;

/** The sessions of this tab, by key; a disposed session leaves it. */
const sessions = new Map();

/**
 * Type size for a pane of this width, in steps. A phone's pane fits some
 * thirty columns at the desktop size, and `ls` sizes its columns from the
 * longest name in the directory, so everything came out in one column.
 * Smaller type buys columns, which is what a terminal needs most; nothing is
 * hidden or wrapped away.
 */
function fontFor(width) {
    if (width > 0 && width < 380) {
        return 10;
    }

    return width > 0 && width < 560 ? 11 : 13;
}

/** Loads xterm.js and its fit addon once, from the agent's own assets. */
function loadEmulator() {
    emulator ??= new Promise((resolve, reject) => {
        const style = document.createElement("link");
        style.rel = "stylesheet";
        style.href = `${VENDOR}/xterm.css`;
        document.head.appendChild(style);

        const load = (src) => new Promise((done, failed) => {
            const script = document.createElement("script");
            script.src = src;
            script.onload = done;
            script.onerror = () => failed(new Error(`Could not load ${src}`));
            document.head.appendChild(script);
        });

        load(`${VENDOR}/xterm.js`)
            .then(() => load(`${VENDOR}/addon-fit.js`))
            .then(resolve)
            .catch(reject);
    });
    return emulator;
}

/**
 * The clipboard API needs a permission the WebView of a native client does not
 * grant, so the copy silently failed there. The old execCommand path still
 * works in every host and is the fallback.
 */
async function toClipboard(text) {
    if (!text) {
        return false;
    }

    try {
        if (navigator.clipboard?.writeText) {
            await navigator.clipboard.writeText(text);
            return true;
        }
    } catch {
        // No permission here: fall through.
    }

    try {
        const box = document.createElement("textarea");
        box.value = text;
        box.setAttribute("readonly", "readonly");
        box.style.position = "fixed";
        box.style.opacity = "0";
        document.body.appendChild(box);
        box.select();
        const copied = document.execCommand("copy");
        document.body.removeChild(box);
        return copied;
    } catch {
        return false;
    }
}

/**
 * Opens the terminal `key` in `element`: the session left by a page visited
 * before, drawn into this pane again with its shell still running, or a new
 * one connected to `url`. `host` is a .NET object reference told about every
 * state change (`OnState(state, text, resumed)`: connecting, connected,
 * closed, error; `resumed` when it is a session taken back, not a new
 * connection) and about anything worth a toast (`OnToast(message, severity)`).
 */
export async function open(element, key, url, command, banner, host) {
    await loadEmulator();
    const kept = sessions.get(key);
    if (kept) {
        kept.attach(element, host);
        return kept;
    }

    const session = new Session(key, element, url, command, banner, host);
    sessions.set(key, session);
    session.connect();
    return session;
}

/** Ends every session of this tab: logging out leaves no shell behind. */
export function closeAll() {
    for (const session of [...sessions.values()]) {
        session.dispose();
    }
}

class Session {
    constructor(key, element, url, command, banner, host) {
        this.key = key;
        this.url = url;
        this.command = command;
        this.banner = banner;
        this.host = host;
        this.socket = null;
        this.connected = false;
        this.starting = false;
        this.backend = "";
        this.pty = true;
        this.cols = 0;
        this.rows = 0;
        this.line = "";
        this.lastWasCr = false;
        this.disposed = false;
        this.last = { state: "closed", text: "Idle" };

        this.term = new window.Terminal({
            cursorBlink: true,
            convertEol: false,
            fontFamily: getComputedStyle(element).fontFamily || "Consolas, 'Courier New', monospace",
            fontSize: fontFor(element.clientWidth),
            lineHeight: 1.15,
            scrollback: 5000,
            theme: THEME,
        });
        this.fit = new window.FitAddon.FitAddon();
        this.term.loadAddon(this.fit);
        this.term.open(element);
        this.term.onData((data) => this.typed(data));
        this.term.attachCustomKeyEventHandler((event) => this.keyed(event));
        this.observe(element);
    }

    /**
     * The pane is measured whenever it changes size, not only when the window
     * does: a dialog animating open, a tab becoming current or a phone rotating
     * all arrive this way. Fitting once at open measured a pane that was not
     * laid out yet and left the shell a few columns wide.
     */
    observe(element) {
        this.observer?.disconnect();
        this.observer = new ResizeObserver(() => this.refit());
        this.observer.observe(element);
        this.refit();
    }

    /**
     * A page opened again takes its session back: the surface moves into the
     * new pane, the new component becomes the host and hears the state the
     * session is in, marked as resumed so a job is not run a second time.
     */
    attach(element, host) {
        this.host = host;
        element.appendChild(this.term.element);
        this.observe(element);
        this.term.refresh(0, this.term.rows - 1);
        this.host?.invokeMethodAsync("OnState", this.last.state, this.last.text, true);
        if (this.connected) {
            this.term.focus();
        }
    }

    /**
     * The page is going away: a live shell stays in the registry, waiting for
     * its next page, with nobody to tell; a shell that has already ended has
     * nothing to keep and is disposed with its page.
     */
    detach() {
        this.host = null;
        this.observer?.disconnect();
        if (!this.connected && !this.starting) {
            this.dispose();
        }
    }

    /** The pane may be hidden (a tab that is not current): nothing to measure. */
    refit() {
        try {
            const size = fontFor(this.term.element?.clientWidth ?? 0);
            if (size !== this.term.options.fontSize) {
                this.term.options.fontSize = size;
            }

            this.fit.fit();
        } catch {
            return;  // Not visible yet.
        }

        const { cols, rows } = this.term;
        if (cols > 0 && rows > 0 && (cols !== this.cols || rows !== this.rows)) {
            this.cols = cols;
            this.rows = rows;
            this.send({ type: "resize", cols, rows });
        }
    }

    state(state, text) {
        this.last = { state, text: text ?? "" };
        this.host?.invokeMethodAsync("OnState", state, text ?? "", false);
    }

    toast(message, severity) {
        this.host?.invokeMethodAsync("OnToast", message, severity ?? "info");
    }

    /** Opens a session; a reconnect passes the command and banner again. */
    connect(command, banner) {
        if (this.disposed || this.starting) {
            return;
        }

        if (command !== undefined && command !== null) {
            this.command = command;
        }

        if (banner !== undefined && banner !== null) {
            this.banner = banner;
        }

        this.disconnect(false, true);
        this.term.reset();
        this.term.options.convertEol = false;
        this.line = "";
        this.backend = "";
        this.pty = true;
        this.starting = true;
        this.state("connecting", "Connecting…");
        if (this.banner) {
            this.writeln(this.banner);
        }

        this.refit();

        let socket;
        try {
            socket = new WebSocket(this.url);
        } catch (error) {
            this.starting = false;
            this.writeln(`\r\n[error] ${error}`);
            this.state("error", "Failed");
            return;
        }

        this.socket = socket;
        socket.onopen = () => {
            const start = { type: "start", cols: this.term.cols, rows: this.term.rows };
            if (this.command) {
                start.command = this.command;
            }

            this.send(start);
        };
        socket.onmessage = (event) => this.received(event.data);
        socket.onerror = () => {
            if (!this.connected) {
                this.writeln("\r\n[error] WebSocket error");
                this.state("error", "Error");
                this.toast("The terminal connection failed.", "error");
            }
        };
        socket.onclose = () => {
            if (this.socket !== socket) {
                return;
            }

            if (this.connected || this.starting) {
                this.writeln("\r\n[disconnected]");
            }

            this.connected = false;
            this.starting = false;
            this.socket = null;
            this.state("closed", "Disconnected");
        };
    }

    received(raw) {
        let message;
        try {
            message = JSON.parse(raw);
        } catch {
            this.term.write(String(raw ?? ""));
            return;
        }

        switch (message.type) {
            case "ready":
                this.connected = true;
                this.starting = false;
                this.backend = String(message.backend ?? "");
                this.pty = typeof message.pty === "boolean" ? message.pty : true;
                // Pipe output carries a bare LF; a pty already emits CRLF.
                this.term.options.convertEol = !this.pty;
                this.state("connected", this.backend ? `Connected (${this.backend})` : "Connected");
                this.refit();
                this.send({ type: "resize", cols: this.term.cols, rows: this.term.rows });
                this.term.focus();
                break;
            case "stdout":
            case "stderr":
                if (message.data) {
                    this.term.write(message.data);
                }

                break;
            case "ping":
                this.send({ type: "pong" });
                break;
            case "pong":
                break;
            case "warning":
                this.writeln(`\r\n[warning] ${message.message ?? ""}`);
                this.toast(message.message ?? "Terminal warning", "info");
                break;
            case "exit":
                this.writeln(`\r\n[process exited${message.code === null || message.code === undefined ? "" : ` with code ${message.code}`}]`);
                this.disconnect(false, true);
                this.state("closed", "Exited");
                break;
            case "error":
                this.writeln(`\r\n[error] ${message.message ?? "unknown error"}`);
                this.toast(message.message ?? "Terminal error", "error");
                this.disconnect(false, true);
                this.state("error", "Error");
                break;
        }
    }

    /**
     * Ctrl+Shift+C always copies; Ctrl+C copies when something is selected and
     * otherwise reaches the shell as SIGINT. Paste is the browser's, so it is
     * never sent twice.
     */
    keyed(event) {
        if (event.type !== "keydown" || !event.ctrlKey || event.altKey || event.metaKey) {
            return true;
        }

        if ((event.key || "").toLowerCase() === "c" && (event.shiftKey || this.term.hasSelection())) {
            event.preventDefault();
            if (this.copySelection()) {
                this.term.clearSelection();
            }

            return false;
        }

        return true;
    }

    copySelection() {
        const text = this.term.getSelection();
        if (!text) {
            return false;
        }

        toClipboard(text).then((copied) => this.toast(copied ? "Copied" : "Could not copy to the clipboard", copied ? "success" : "error"));
        return true;
    }

    /**
     * A keystroke from the keyboard. Ctrl, Alt or Shift held on the row under
     * the pane (latch) change the next single key typed, as a real keyboard's would,
     * and are let go after it; the host hears it, so the row shows them up.
     */
    typed(data) {
        if (this.latched) {
            const latched = this.latched;
            // Ctrl and Alt are let go after the key; Shift stays locked until the row lets it go.
            this.latched = latched.shift ? { ctrl: false, shift: true, alt: false } : null;
            if (latched.ctrl || latched.alt) {
                this.host?.invokeMethodAsync("OnModifiersUsed");
            }

            // Ctrl from the row with A selects the terminal's text, as Ctrl+A
            // does in an editor, for the rail's Copy: on a phone there is no
            // other way to select it all. A keyboard's own Ctrl+A never comes
            // through here and stays the shell's (the start of the line).
            if (latched.ctrl && !latched.alt && data.toLowerCase() === "a") {
                this.term.selectAll();
                return;
            }

            if (data.length === 1) {
                data = this.modified(data, latched);
            }
        }

        this.input(data);
    }

    /** Ctrl, Alt and Shift held on the row for the next key typed. */
    latch(ctrl, shift, alt) {
        this.latched = ctrl || shift || alt ? { ctrl: !!ctrl, shift: !!shift, alt: !!alt } : null;
        this.term.focus();
    }

    /**
     * One typed character with the held modifiers: upper case with Shift, its
     * control code with Ctrl (Ctrl+C is ETX), Esc before it with Alt (the Meta
     * a real keyboard sends: Alt+B is a word back in bash).
     */
    modified(char, latched) {
        let result = latched.shift ? char.toUpperCase() : char;
        if (latched.ctrl) {
            const code = result.toUpperCase().charCodeAt(0);
            if (code >= 64 && code <= 95) {
                result = String.fromCharCode(code - 64);
            } else if (result === " ") {
                result = "\x00";
            } else if (result === "?") {
                result = "\x7f";
            }
        }

        return latched.alt ? `\x1b${result}` : result;
    }

    /** What goes to the shell: straight through with a pty, line by line behind a pipe. */
    input(data) {
        if (!this.connected) {
            return;
        }

        if (this.pty) {
            this.send({ type: "stdin", data });
            return;
        }

        if (data.startsWith("")) {
            return;  // Cursor keys and friends: no line editing to do here.
        }

        for (const char of data) {
            const wasCr = this.lastWasCr;
            this.lastWasCr = char === "\r";
            if (char === "\r") {
                this.submit();
            } else if (char === "\n") {
                if (!wasCr) {
                    this.submit();
                }
            } else if (char === "" || char === "\b") {
                if (this.line) {
                    this.line = this.line.slice(0, -1);
                    this.term.write("\b \b");
                }
            } else if (char === "") {
                this.line = "";
                this.term.write("^C\r\n");
                this.send({ type: "stdin", data: "" });
            } else if (char === "") {
                this.send({ type: "stdin", data: "" });
            } else if (char === "") {
                this.eraseLine();
            } else if (char === "") {
                this.term.clear();
            } else if (char === "\t") {
                // No completion without a pty; leave the line as it is.
            } else if (char >= " ") {
                this.line += char;
                this.term.write(char);
            }
        }
    }

    submit() {
        const line = this.line;
        this.line = "";
        this.term.write("\r\n");
        // A POSIX shell takes LF; a Windows shell over pipes wants CRLF.
        this.send({ type: "stdin", data: line + (this.backend === "local-shell" ? "\r\n" : "\n") });
    }

    eraseLine() {
        if (!this.line) {
            return;
        }

        this.term.write("\b \b".repeat(this.line.length));
        this.line = "";
    }

    writeln(text) {
        this.term.write(`${String(text).replace(/\r?\n/g, "\r\n")}\r\n`);
    }

    send(payload) {
        if (this.socket?.readyState !== WebSocket.OPEN) {
            return false;
        }

        try {
            this.socket.send(JSON.stringify(payload));
            return true;
        } catch {
            return false;
        }
    }

    /**
     * A key pressed on the row under the pane (ControlKeys), with the Ctrl,
     * Alt and Shift held for it, sent as the keyboard would send it: the arrows, Home
     * and End in the form the shell asked for (application cursor mode, as a
     * full-screen program sets it) or, with a modifier, xterm's CSI 1;m form
     * (Ctrl+→ is a word to the right); Shift+Tab is the back tab. The focus
     * goes back on the terminal so typing goes on where it was.
     */
    press(name, ctrl, shift, alt) {
        const modifier = 1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (ctrl ? 4 : 0);
        const application = this.term.modes?.applicationCursorKeysMode;
        const cursor = (final) => modifier > 1 ? `\x1b[1;${modifier}${final}` : `${application ? "\x1bO" : "\x1b["}${final}`;
        const sequence = {
            Escape: alt ? "\x1b\x1b" : "\x1b",
            Tab: `${alt ? "\x1b" : ""}${shift ? "\x1b[Z" : "\t"}`,
            Up: cursor("A"),
            Down: cursor("B"),
            Right: cursor("C"),
            Left: cursor("D"),
            Home: cursor("H"),
            End: cursor("F"),
        }[name];
        this.latched = shift ? { ctrl: false, shift: true, alt: false } : null;
        if (sequence) {
            this.input(sequence);
        }

        this.term.focus();
    }

    /** Types `text` into the shell as if the user had: a terminal job's command line. */
    type(text) {
        return this.send({ type: "stdin", data: String(text ?? "") });
    }

    clear() {
        this.term.clear();
    }

    focus() {
        this.term.focus();
    }

    disconnect(sendClose = true, quiet = false) {
        this.connected = false;
        this.starting = false;
        this.line = "";
        const socket = this.socket;
        this.socket = null;
        if (socket) {
            try {
                if (sendClose && socket.readyState === WebSocket.OPEN) {
                    socket.send(JSON.stringify({ type: "close" }));
                }
            } catch {
                // Already gone.
            }

            try {
                socket.close();
            } catch {
                // Already gone.
            }
        }

        if (!quiet) {
            this.state("closed", "Disconnected");
        }
    }

    dispose() {
        if (this.disposed) {
            return;
        }

        this.disposed = true;
        sessions.delete(this.key);
        this.disconnect(true, true);
        this.observer?.disconnect();
        try {
            this.term.dispose();
        } catch {
            // Already gone with the page.
        }
    }
}
