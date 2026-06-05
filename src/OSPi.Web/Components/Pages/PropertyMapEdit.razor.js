// Sizes the property-map editor canvas to the largest image-ratio rectangle that fits the
// available area (stage width x viewport height), and reports click placements as
// normalized [0,1] coordinates computed from the canvas's live size — so C# no longer needs
// to know the rendered px size. No pan/zoom here; the editor is just full-size.

export function init(canvas, ratio, dotNet) {
    const stage = canvas.parentElement;
    const controller = new AbortController();
    const opts = { signal: controller.signal };

    // Largest image-ratio rect fitting stage width x available viewport height.
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
        canvas.style.width = `${Math.round(w)}px`;
        canvas.style.height = `${Math.round(h)}px`;
    }

    // Normalize against the element's live size at click time, so placement stays accurate
    // after any resize without C# tracking the px dimensions.
    function onClick(e) {
        const rect = canvas.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) {
            return;
        }
        const nx = (e.clientX - rect.left) / rect.width;
        const ny = (e.clientY - rect.top) / rect.height;
        dotNet.invokeMethodAsync('PlaceNormalized', nx, ny);
    }

    canvas.addEventListener('click', onClick, opts);

    const ro = new ResizeObserver(() => fit());
    ro.observe(stage);
    window.addEventListener('resize', fit, opts);
    fit();

    canvas._mapEditFit = { dispose: () => { ro.disconnect(); controller.abort(); } };
}

export function dispose(canvas) {
    canvas?._mapEditFit?.dispose();
}
