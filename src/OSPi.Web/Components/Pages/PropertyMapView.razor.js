// Client-side pan/zoom for the property map. Runs entirely in the browser so pointer
// moves never round-trip the Blazor Server circuit; only pin clicks reach C#. The
// container is the full available viewport; [data-map-content] is the fitted image box
// (cw x ch) panned/zoomed within it via a CSS transform. That content element is
// static-styled (Blazor's diff never rewrites it), so percentage-positioned markers stay
// aligned at every zoom level and the --pin-scale custom property survives pin re-renders.

const MIN_SCALE = 1;
const MAX_SCALE = 6;
const TAP_SLOP = 8; // px of movement below which a gesture is still treated as a tap

export function init(container, ratio) {
    const content = container.querySelector('[data-map-content]');
    if (!content) {
        return;
    }

    const stage = container.parentElement;

    const controller = new AbortController();
    const opts = { signal: controller.signal };

    let scale = 1;
    let tx = 0;
    let ty = 0;

    // Geometry set by fit(): content base box (cw x ch) and viewport (vw x vh).
    let cw = 0;
    let ch = 0;
    let vw = 0;
    let vh = 0;

    const pointers = new Map(); // pointerId -> { x, y }
    let dragStart = null;       // { x, y, tx, ty } for single-pointer pan
    let lastDist = 0;           // last pinch distance
    let moved = 0;              // accumulated movement of the active gesture

    // Clamp/center one axis. rendered = base*scale. When the content is no larger than the
    // viewport on that axis it is centered (and locked); otherwise translation is clamped
    // so an edge never pulls inside the viewport.
    function clampAxis(t, base, viewport) {
        const rendered = base * scale;
        if (rendered <= viewport) {
            return (viewport - rendered) / 2;
        }
        return Math.min(0, Math.max(viewport - rendered, t));
    }

    function apply() {
        tx = clampAxis(tx, cw, vw);
        ty = clampAxis(ty, ch, vh);
        content.style.transform = `translate(${tx}px, ${ty}px) scale(${scale})`;
        // Counter-scale pins so they keep a constant on-screen size while zooming.
        content.style.setProperty('--pin-scale', `${1 / scale}`);
    }

    // The container fills the full available area (stage width x viewport height below the
    // stage top). [data-map-content] is sized to the largest image-ratio rect fitting that
    // area. Re-clamp afterward so a shrink never strands panned content off-box.
    function fit() {
        vw = stage.clientWidth;
        vh = Math.max(0, window.innerHeight - stage.getBoundingClientRect().top - 16);
        if (vw <= 0 || vh <= 0) {
            return;
        }
        let w = vw;
        let h = w / ratio;
        if (h > vh) {
            h = vh;
            w = h * ratio;
        }
        cw = Math.round(w);
        ch = Math.round(h);

        container.style.width = `${vw}px`;
        container.style.height = `${vh}px`;
        content.style.width = `${cw}px`;
        content.style.height = `${ch}px`;
        apply();
    }

    // Zoom toward a focal point (cursor or pinch midpoint) so the point under the focus
    // stays put. focus is in viewport-local coords (clientX - container.left).
    function zoomAt(clientX, clientY, factor) {
        const rect = container.getBoundingClientRect();
        const fx = clientX - rect.left;
        const fy = clientY - rect.top;
        const next = Math.min(MAX_SCALE, Math.max(MIN_SCALE, scale * factor));
        if (next === scale) {
            return;
        }
        tx = fx - (fx - tx) * (next / scale);
        ty = fy - (fy - ty) * (next / scale);
        scale = next;
        apply();
    }

    function dist() {
        const [a, b] = [...pointers.values()];
        return Math.hypot(a.x - b.x, a.y - b.y);
    }

    function midpoint() {
        const [a, b] = [...pointers.values()];
        return { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 };
    }

    // True when either axis can pan (content larger than the viewport on that axis).
    function pannable() {
        return cw * scale > vw + 0.5 || ch * scale > vh + 0.5;
    }

    function onWheel(e) {
        e.preventDefault();
        zoomAt(e.clientX, e.clientY, e.deltaY < 0 ? 1.1 : 1 / 1.1);
    }

    function onPointerDown(e) {
        // Deliberately no setPointerCapture: capturing on the container retargets the
        // follow-up `click` away from the marker, so taps would never open the dialog.
        // We track moves/releases on window instead, which also keeps a pan alive when
        // the cursor leaves the map.
        pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
        moved = 0;
        if (pointers.size === 1) {
            dragStart = { x: e.clientX, y: e.clientY, tx, ty };
        } else if (pointers.size === 2) {
            dragStart = null;
            lastDist = dist();
        }
    }

    function onPointerMove(e) {
        if (!pointers.has(e.pointerId)) {
            return;
        }
        pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });

        if (pointers.size >= 2) {
            const d = dist();
            if (lastDist > 0) {
                const mid = midpoint();
                zoomAt(mid.x, mid.y, d / lastDist);
            }
            lastDist = d;
            moved = TAP_SLOP + 1; // a pinch is never a tap
        } else if (dragStart && pannable()) {
            const dx = e.clientX - dragStart.x;
            const dy = e.clientY - dragStart.y;
            moved = Math.max(moved, Math.abs(dx) + Math.abs(dy));
            // clampAxis() (in apply) keeps centered/locked axes from drifting.
            tx = dragStart.tx + dx;
            ty = dragStart.ty + dy;
            apply();
        }
    }

    function onPointerUp(e) {
        pointers.delete(e.pointerId);
        if (pointers.size < 2) {
            lastDist = 0;
        }
        if (pointers.size === 0) {
            dragStart = null;
            // A real drag/pinch shouldn't open a zone dialog: swallow the click that
            // follows. A stationary tap (moved <= slop) falls through to the marker.
            if (moved > TAP_SLOP) {
                container.addEventListener(
                    'click',
                    (ev) => {
                        ev.stopPropagation();
                        ev.preventDefault();
                    },
                    { capture: true, once: true });
            }
        }
    }

    container.addEventListener('wheel', onWheel, { ...opts, passive: false });
    container.addEventListener('pointerdown', onPointerDown, opts);
    // Move/up on window so a drag keeps tracking outside the map and the `click` that
    // follows a tap still reaches the marker (see onPointerDown).
    window.addEventListener('pointermove', onPointerMove, opts);
    window.addEventListener('pointerup', onPointerUp, opts);
    window.addEventListener('pointercancel', onPointerUp, opts);

    // Keep the viewport fitted to the available area. The ResizeObserver catches stage
    // width changes (window resize, nav drawer toggle); the resize listener also catches
    // things that move the stage's top (toolbar wrap, orientation, mobile URL-bar toggle).
    const ro = new ResizeObserver(() => fit());
    ro.observe(stage);
    window.addEventListener('resize', fit, opts);
    fit();

    container._mapPanZoom = { dispose: () => { ro.disconnect(); controller.abort(); } };
}

export function dispose(container) {
    container?._mapPanZoom?.dispose();
}
