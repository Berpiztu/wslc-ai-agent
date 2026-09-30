// The metrics line chart: a canvas that fills its
// panel, five horizontal and eight vertical grid lines, a formatted Y axis, up to
// six clock labels, one line per series, and on hover a dashed cursor, the
// points and a tooltip with the time and every value. Colours and the label size
// are the theme's, read when drawing, so light and dark both hold.
//
//   const chart = create(host, { yMin, yMax, forceYMax, format: "percent" | "bytes", series: [{ label, colour }] });
//   chart.update(times, values, forceYMax);   // times: Unix ms; values: one array per series
//   chart.zoomIn(); chart.zoomOut(); chart.zoomReset();   // fewer or more of the latest points
//   chart.dispose();

const PALETTE = {
    primary: "--mud-palette-info",
    secondary: "--mud-palette-warning",
};

function cssVar(name, fallback) {
    const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    return value || fallback;
}

function clock(ms, seconds) {
    const d = new Date(ms);
    const two = (n) => String(n).padStart(2, "0");
    return seconds ? `${two(d.getHours())}:${two(d.getMinutes())}:${two(d.getSeconds())}` : `${two(d.getHours())}:${two(d.getMinutes())}`;
}

/** Compact bytes: 2 decimals under ten of a unit, 1 above. */
function bytes(n) {
    const v = Number(n) || 0;
    const abs = Math.abs(v);
    const units = [["TB", 1024 ** 4], ["GB", 1024 ** 3], ["MB", 1024 ** 2], ["KB", 1024]];
    for (const [unit, size] of units) {
        if (abs >= size) {
            return `${(v / size).toFixed(abs < 10 * size || unit === "TB" ? 2 : 1)}${unit}`;
        }
    }
    return `${Math.round(v)}B`;
}

/** The chart window: zoom shows the latest max(8, 120 / zoom) points. */
const MAX_POINTS = 120;
const MIN_POINTS = 8;

const FORMATS = {
    percent: { axis: (v) => `${Math.round(v)}%`, value: (v) => `${v.toFixed(2)}%` },
    bytes: { axis: bytes, value: bytes },
};

let tooltip = null;

function showTooltip(clientX, clientY, time, rows) {
    if (!tooltip) {
        tooltip = document.createElement("div");
        tooltip.className = "wslc-chart-tooltip";
        document.body.appendChild(tooltip);
    }
    tooltip.replaceChildren();
    const head = document.createElement("div");
    head.className = "wslc-chart-tooltip-time";
    head.textContent = clock(time, true);
    tooltip.appendChild(head);
    for (const row of rows) {
        const line = document.createElement("div");
        const swatch = document.createElement("span");
        swatch.className = "wslc-chart-tooltip-swatch";
        swatch.style.background = row.colour;
        line.append(swatch, document.createTextNode(`${row.label}: ${row.text}`));
        tooltip.appendChild(line);
    }
    tooltip.hidden = false;
    const pad = 12;
    tooltip.style.left = `${clientX + pad}px`;
    tooltip.style.top = `${clientY + pad}px`;
    const rect = tooltip.getBoundingClientRect();
    const left = rect.right > window.innerWidth - 8 ? clientX - rect.width - pad : clientX + pad;
    const top = rect.bottom > window.innerHeight - 8 ? clientY - rect.height - pad : clientY + pad;
    tooltip.style.left = `${Math.max(8, left)}px`;
    tooltip.style.top = `${Math.max(8, top)}px`;
}

function hideTooltip() {
    if (tooltip) {
        tooltip.hidden = true;
    }
}

/** The Y range: a little headroom, a fixed ceiling kept only once the data comes near it. */
function yRange(options, arrays) {
    let lo = Infinity;
    let hi = -Infinity;
    for (const values of arrays) {
        for (const v of values) {
            if (Number.isFinite(v)) {
                lo = Math.min(lo, v);
                hi = Math.max(hi, v);
            }
        }
    }
    if (!Number.isFinite(lo) || !Number.isFinite(hi)) {
        lo = 0;
        hi = 1;
    }
    if (lo === hi) {
        const base = Math.abs(lo) || 1;
        hi = lo + base * 0.15;
        lo = Math.max(0, lo - base * 0.05);
    }
    const pad = (hi - lo) * 0.12 || 1;
    lo = Math.max(0, lo - pad * 0.05);
    hi += pad;
    if (options.yMin != null) {
        lo = options.yMin;
    }
    if (options.yMax != null) {
        hi = hi < options.yMax * 0.25 ? Math.max(hi, options.yMax * 0.05, 1) : Math.max(hi, options.yMax);
    }
    if (options.forceYMax != null) {
        const limit = options.forceYMax;
        hi = hi >= limit * 0.2 ? Math.max(hi, limit) : Math.max(hi, Math.min(limit, hi * 1.4));
    }
    return { min: lo, max: Math.max(hi, lo + 1e-9) };
}

export function create(host, options) {
    // The panel may be gone by the time this module finishes loading: the app
    // opens on Home, draws its charts and is sent to the sign-in screen while
    // the import is still in flight, and the element the caller named no longer
    // exists. There is nothing to draw on, and that is not an error.
    if (!host) {
        return null;
    }

    const canvas = host.querySelector("canvas");
    const format = FORMATS[options.format] || FORMATS.percent;
    let allTimes = [];
    let allValues = [];
    let times = [];
    let values = [];
    let forceYMax = null;
    let zoom = 1;
    let hover = -1;
    let layout = null;

    /** The visible window: every point up to two, then the latest max(8, 120 / zoom). */
    function slice() {
        const n = allTimes.length;
        const visible = n <= 2 ? n : Math.min(n, Math.max(MIN_POINTS, Math.floor(MAX_POINTS / zoom)));
        times = allTimes.slice(n - visible);
        values = allValues.map((data) => data.slice(Math.max(0, data.length - visible)));
    }

    function colourOf(index) {
        const name = options.series[index]?.colour === "secondary" ? PALETTE.secondary : PALETTE.primary;
        return cssVar(name, index === 0 ? "#3b82f6" : "#b45309");
    }

    function draw() {
        const dpr = window.devicePixelRatio || 1;
        const cssW = Math.max(10, host.clientWidth);
        const cssH = Math.max(10, host.clientHeight);
        if (canvas.width !== Math.floor(cssW * dpr) || canvas.height !== Math.floor(cssH * dpr)) {
            canvas.width = Math.floor(cssW * dpr);
            canvas.height = Math.floor(cssH * dpr);
            canvas.style.width = `${cssW}px`;
            canvas.style.height = `${cssH}px`;
        }
        const ctx = canvas.getContext("2d");
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        ctx.clearRect(0, 0, cssW, cssH);

        const range = yRange({ ...options, forceYMax }, values);
        const span = range.max - range.min || 1;
        const grid = cssVar("--mud-palette-lines-default", "#334155");
        const muted = cssVar("--mud-palette-text-secondary", "#94a3b8");
        const text = cssVar("--mud-palette-text-primary", "#f8fafc");
        // The panel's own font size: the theme's caption by default (the
        // stylesheet gives .wslc-chart-canvas that), a step up or down when
        // the dashboard part the chart stands in has a type size of its own.
        const size = getComputedStyle(host).fontSize || cssVar("--mud-typography-caption-size", "0.72rem");
        const family = getComputedStyle(host).fontFamily;
        ctx.font = `${size} ${family}`;
        const axis = Array.from({ length: 6 }, (_, i) => format.axis(range.max - (span * i) / 5));

        // The left margin is what this chart's own labels take, not one width
        // for every chart (a percent chart's
        // "23%" sat far from the edge in the room a disk chart's "2.51GB"
        // needs): the widest value label and the 6px between it and the grid,
        // or half the first time label, which is centred on the grid's edge.
        const padL = Math.ceil(Math.max(
            Math.max(...axis.map((label) => ctx.measureText(label).width)) + 6,
            ctx.measureText("00:00").width / 2 + 2));
        const padR = 18;
        const padT = 10;
        const padB = 22;
        const plotW = Math.max(1, cssW - padL - padR);
        const plotH = Math.max(1, cssH - padT - padB);

        ctx.strokeStyle = grid;
        ctx.lineWidth = 1;
        for (let i = 0; i <= 5; i++) {
            const y = padT + (plotH * i) / 5;
            ctx.beginPath();
            ctx.moveTo(padL, y);
            ctx.lineTo(padL + plotW, y);
            ctx.stroke();
        }
        for (let i = 0; i <= 8; i++) {
            const x = padL + (plotW * i) / 8;
            ctx.beginPath();
            ctx.moveTo(x, padT);
            ctx.lineTo(x, padT + plotH);
            ctx.stroke();
        }

        ctx.fillStyle = muted;
        ctx.textAlign = "right";
        ctx.textBaseline = "middle";
        axis.forEach((label, i) => ctx.fillText(label, padL - 6, padT + (plotH * i) / 5));

        const xAt = (i, n) => (n <= 1 ? padL : padL + (plotW * i) / (n - 1));
        const yAt = (v) => padT + plotH - ((v - range.min) / span) * plotH;

        if (times.length) {
            ctx.textAlign = "center";
            ctx.textBaseline = "top";
            const labels = Math.min(6, times.length);
            for (let i = 0; i < labels; i++) {
                const idx = Math.round((i * (times.length - 1)) / Math.max(1, labels - 1));
                ctx.fillText(clock(times[idx], false), xAt(idx, times.length), padT + plotH + 4);
            }
        }

        values.forEach((data, s) => {
            if (!data.length) {
                return;
            }
            ctx.strokeStyle = colourOf(s);
            ctx.fillStyle = colourOf(s);
            ctx.lineWidth = 2;
            ctx.lineJoin = "round";
            ctx.lineCap = "round";
            if (data.length === 1) {
                ctx.beginPath();
                ctx.arc(xAt(0, 1), yAt(data[0]), 3.5, 0, Math.PI * 2);
                ctx.fill();
                return;
            }
            ctx.beginPath();
            data.forEach((v, i) => (i === 0 ? ctx.moveTo(xAt(i, data.length), yAt(v)) : ctx.lineTo(xAt(i, data.length), yAt(v))));
            ctx.stroke();
        });

        if (hover >= 0 && times.length) {
            const idx = Math.min(hover, times.length - 1);
            const x = xAt(idx, times.length);
            ctx.strokeStyle = muted;
            ctx.lineWidth = 1;
            ctx.setLineDash([4, 3]);
            ctx.beginPath();
            ctx.moveTo(x, padT);
            ctx.lineTo(x, padT + plotH);
            ctx.stroke();
            ctx.setLineDash([]);
            values.forEach((data, s) => {
                if (!Number.isFinite(data[idx])) {
                    return;
                }
                ctx.fillStyle = colourOf(s);
                ctx.beginPath();
                ctx.arc(x, yAt(data[idx]), 3.5, 0, Math.PI * 2);
                ctx.fill();
                ctx.strokeStyle = text;
                ctx.lineWidth = 1;
                ctx.stroke();
            });
        }

        layout = { padL, plotW };
    }

    function onMove(event) {
        if (!layout || !times.length) {
            return;
        }
        const rel = event.clientX - canvas.getBoundingClientRect().left - layout.padL;
        if (rel < 0 || rel > layout.plotW) {
            onLeave();
            return;
        }
        hover = Math.max(0, Math.min(times.length - 1, Math.round((rel / layout.plotW) * Math.max(0, times.length - 1))));
        draw();
        showTooltip(event.clientX, event.clientY, times[hover], values.map((data, s) => ({
            label: options.series[s]?.label || "",
            colour: colourOf(s),
            text: Number.isFinite(data[hover]) ? format.value(data[hover]) : "—",
        })));
    }

    function onLeave() {
        hover = -1;
        hideTooltip();
        draw();
    }

    const observer = new ResizeObserver(() => draw());
    observer.observe(host);
    canvas.addEventListener("mousemove", onMove);
    canvas.addEventListener("mouseleave", onLeave);
    draw();

    return {
        update(nextTimes, nextValues, nextForceYMax) {
            allTimes = nextTimes;
            allValues = nextValues;
            forceYMax = nextForceYMax > 0 ? nextForceYMax : null;
            slice();
            draw();
        },
        zoomIn() {
            zoom = Math.min(8, zoom * 1.5);
            slice();
            draw();
        },
        zoomOut() {
            zoom = Math.max(1, zoom / 1.5);
            slice();
            draw();
        },
        zoomReset() {
            zoom = 1;
            slice();
            draw();
        },
        dispose() {
            observer.disconnect();
            canvas.removeEventListener("mousemove", onMove);
            canvas.removeEventListener("mouseleave", onLeave);
            hideTooltip();
        },
    };
}
