namespace WslcAgent.Server.Browse;

/// <summary>
/// What the host browser evaluates inside the page: what took focus, what is
/// selected and where, and the scroll that brings a covered field back. They
/// read the DOM the screencast cannot carry.
/// </summary>
internal static class PageScripts
{
    /// <summary><c>{editable, inputType}</c> for the focused element: the pane opens the soft keyboard only for a text field.</summary>
    public const string Focus = """
        () => {
            const el = document.activeElement;
            if (!el) return { editable: false };
            if (el.isContentEditable) return { editable: true, inputType: 'text' };
            const tag = String(el.tagName || '').toLowerCase();
            if (tag === 'textarea') return { editable: true, inputType: 'text' };
            if (tag !== 'input') return { editable: false };
            const t = String(el.type || 'text').toLowerCase();
            const notText = ['button', 'submit', 'reset', 'checkbox', 'radio', 'file', 'color', 'range',
                'image', 'hidden', 'date', 'datetime-local', 'month', 'week', 'time'];
            if (notText.includes(t) || el.readOnly || el.disabled) return { editable: false };
            return { editable: true, inputType: t };
        }
        """;

    /// <summary>The selected text: inside the focused field, else the page selection.</summary>
    public const string SelectionText = """
        () => {
            const el = document.activeElement;
            const tag = el ? String(el.tagName || '').toLowerCase() : '';
            if ((tag === 'input' || tag === 'textarea') && typeof el.selectionStart === 'number'
                && el.selectionEnd > el.selectionStart) {
                return String(el.value).slice(el.selectionStart, el.selectionEnd);
            }
            const sel = window.getSelection ? window.getSelection() : null;
            return sel ? String(sel.toString()) : '';
        }
        """;

    /// <summary>
    /// Selection geometry in viewport CSS px plus its text, or <c>{empty:true}</c>.
    /// With a handle (<c>start</c> or <c>end</c>) the selection is re-anchored so
    /// that a following shift+click moves that end. A field's selection is
    /// measured on a hidden mirror of it, since a field has no text ranges.
    /// </summary>
    public const string Selection = """
        (handle) => {
            const el = document.activeElement;
            const tag = el ? String(el.tagName || '').toLowerCase() : '';
            const isControl = (tag === 'input' || tag === 'textarea') && typeof el.selectionStart === 'number';
            if (isControl) {
                const s0 = el.selectionStart, e0 = el.selectionEnd;
                if (e0 <= s0) return { empty: true };
                if (handle) el.setSelectionRange(s0, e0, handle === 'start' ? 'backward' : 'forward');
                const cs = getComputedStyle(el);
                const m = document.createElement('div');
                for (const prop of ['fontFamily', 'fontSize', 'fontWeight', 'fontStyle', 'letterSpacing',
                    'textTransform', 'wordSpacing', 'textIndent', 'paddingTop', 'paddingRight', 'paddingBottom',
                    'paddingLeft', 'borderTopWidth', 'borderRightWidth', 'borderBottomWidth', 'borderLeftWidth',
                    'boxSizing', 'lineHeight', 'tabSize']) {
                    m.style[prop] = cs[prop];
                }
                const er = el.getBoundingClientRect();
                m.style.position = 'absolute';
                m.style.visibility = 'hidden';
                m.style.top = '0';
                m.style.left = '-9999px';
                m.style.width = er.width + 'px';
                m.style.height = er.height + 'px';
                m.style.overflow = 'hidden';
                m.style.whiteSpace = tag === 'textarea' ? 'pre-wrap' : 'pre';
                m.style.wordWrap = 'break-word';
                if (tag === 'input') {
                    m.style.display = 'flex';
                    m.style.alignItems = 'center';
                }
                const wrap = document.createElement('span');
                const v = String(el.value);
                wrap.appendChild(document.createTextNode(v.slice(0, s0)));
                const mid = document.createElement('span');
                mid.textContent = v.slice(s0, e0);
                wrap.appendChild(mid);
                wrap.appendChild(document.createTextNode(v.slice(e0)));
                m.appendChild(wrap);
                document.body.appendChild(m);
                const mr = m.getBoundingClientRect();
                const rects = Array.from(mid.getClientRects());
                m.remove();
                if (!rects.length) return { empty: true };
                const f = rects[0], l = rects[rects.length - 1];
                const dx = er.left - mr.left - el.scrollLeft;
                const dy = er.top - mr.top - el.scrollTop;
                return {
                    start: { x: f.left + dx, y: f.top + dy, h: f.height },
                    end: { x: l.right + dx, y: l.top + dy, h: l.height },
                    text: v.slice(s0, e0),
                };
            }
            const sel = window.getSelection ? window.getSelection() : null;
            if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return { empty: true };
            const range = sel.getRangeAt(0);
            if (handle === 'start') {
                sel.setBaseAndExtent(range.endContainer, range.endOffset, range.startContainer, range.startOffset);
            } else if (handle === 'end') {
                sel.setBaseAndExtent(range.startContainer, range.startOffset, range.endContainer, range.endOffset);
            }
            const rects = Array.from(range.getClientRects()).filter((r) => r.width || r.height);
            if (!rects.length) return { empty: true };
            const f = rects[0], l = rects[rects.length - 1];
            return {
                start: { x: f.left, y: f.top, h: f.height },
                end: { x: l.right, y: l.top, h: l.height },
                text: sel.toString(),
            };
        }
        """;

    /// <summary>After a resize: a focused editable that left the viewport is centred again. The page never moves on a plain click.</summary>
    public const string RevealIfCovered = """
        () => {
            const el = document.activeElement;
            if (!el || el === document.body || el === document.documentElement) return false;
            const tag = String(el.tagName || '').toLowerCase();
            const editable = el.isContentEditable || tag === 'input' || tag === 'textarea' || tag === 'select';
            if (!editable) return false;
            const r = el.getBoundingClientRect();
            const h = window.innerHeight || document.documentElement.clientHeight || 0;
            const w = window.innerWidth || document.documentElement.clientWidth || 0;
            if (!(r.bottom > h || r.top < 0 || r.right > w || r.left < 0)) return false;
            el.scrollIntoView({ block: 'center', inline: 'nearest', behavior: 'instant' });
            return true;
        }
        """;

    /// <summary>
    /// Browser-style zoom as CSS <c>zoom</c> on the root element, now and on every
    /// new document. The screencast stays viewport-sized and pointer
    /// coordinates stay in viewport px, so nothing on the pane converts.
    /// </summary>
    public static string Zoom(string value) => $$"""
        (function () {
            const apply = () => { const r = document.documentElement; if (r) r.style.zoom = '{{value}}'; };
            apply();
            document.addEventListener('DOMContentLoaded', apply);
        })();
        """;
}
