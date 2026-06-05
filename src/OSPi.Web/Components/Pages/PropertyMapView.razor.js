// Client-side pan/zoom for the property map. Runs entirely in the browser so pointer
// moves never round-trip the Blazor Server circuit; only pin clicks reach C#. Applies a
// CSS transform to the [data-map-content] wrapper (a static-styled element Blazor's diff
// never rewrites), keeping percentage-positioned markers aligned at every zoom level.

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

    const pointers = new Map(); // pointerId -> { x, y }
    let dragStart = null;       // { x, y, tx, ty } for single-pointer pan
    let lastDist = 0;           // last pinch distance
    let moved = 0;              // accumulated movement of the active gesture

    function apply() {
        const rect = container.getBoundingClientRect();
        // Clamp so the scaled content never pulls its edges inside the viewport.
        const minX = rect.width * (1 - scale);
        const minY = rect.height * (1 - scale);
        tx = Math.min(0, Math.max(minX, tx));
        ty = Math.min(0, Math.max(minY, ty));
        content.style.transform = `translate(${tx}px, ${ty}px) scale(${scale})`;
    }

    // Size the box to the largest image-aspect-ratio rectangle that fits the available
    // area (stage width × viewport height below the stage's top), then re-clamp the
    // current transform so a shrink never strands the panned content off-box.
    function fit() {
        const aw = stage.clientWidth;
        const ah = Math.max(0, window.innerHeight - stage.getBoundingClientRect().top - 16);
        if (aw <= 0 || ah <= 0) {
            return;
        }
        let w = aw;
        let h = w / ratio;
        if (h > ah) {
            h = ah;
            w = h * ratio;
        }
        container.style.width = `${Math.round(w)}px`;
        container.style.height = `${Math.round(h)}px`;
        apply();
    }

    // Zoom toward a focal point (cursor or pinch midpoint) so the point under the focus
    // stays put.
    function zoomAt(clientX, clientY, factor) {
        const rect = container.getBoundingClientRect();
        const px = clientX - rect.left;
        const py = clientY - rect.top;
        const next = Math.min(MAX_SCALE, Math.max(MIN_SCALE, scale * factor));
        if (next === scale) {
            return;
        }
        tx = px - (px - tx) * (next / scale);
        ty = py - (py - ty) * (next / scale);
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
        } else if (dragStart && scale > 1) {
            const dx = e.clientX - dragStart.x;
            const dy = e.clientY - dragStart.y;
            moved = Math.max(moved, Math.abs(dx) + Math.abs(dy));
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

    // Keep the box fitted to the available area. The ResizeObserver catches stage width
    // changes (window resize, nav drawer toggle); the resize listener also catches things
    // that move the stage's top (toolbar wrap, orientation, mobile URL-bar show/hide).
    const ro = new ResizeObserver(() => fit());
    ro.observe(stage);
    window.addEventListener('resize', fit, opts);
    fit();

    container._mapPanZoom = { dispose: () => { ro.disconnect(); controller.abort(); } };
}

export function dispose(container) {
    container?._mapPanZoom?.dispose();
}
