function pin(el, left, top) {
  el.style.transform = 'none';
  el.style.right = 'auto';
  el.style.bottom = 'auto';
  el.style.left = left + 'px';
  el.style.top = top + 'px';
  const r = el.getBoundingClientRect();
  const dx = left - r.left, dy = top - r.top;
  if (dx) el.style.left = left + dx + 'px';
  if (dy) el.style.top = top + dy + 'px';
  return { dx, dy };
}

export function place(el, left, top, width, height) {
  if (!el) return;
  try {
    if (el.hasAttribute('popover') && !el.matches(':popover-open')) el.showPopover();
  } catch {
  }
  if (width != null) el.style.width = width + 'px';
  if (height != null) el.style.height = height + 'px';
  const r = el.getBoundingClientRect();
  pin(el, clamp(left == null ? r.left : left, window.innerWidth - 40),
      clamp(top == null ? r.top : top, window.innerHeight - 40));
}

function clamp(v, max) {
  return Math.max(0, Math.min(max, v));
}

export function makeDraggable(el, handle, sink) {
  if (!el || !handle) return { dispose() {} };

  let startX = 0, startY = 0, baseLeft = 0, baseTop = 0, offX = 0, offY = 0, dragging = false;

  const report = () => {
    if (!sink) return;
    const r = el.getBoundingClientRect();
    try {
      sink.invokeMethodAsync('OnFloatChanged', r.left, r.top, r.width, r.height);
    } catch {
    }
  };

  const onMove = ev => {
    if (!dragging) return;
    const left = baseLeft + (ev.clientX - startX);
    const top = baseTop + (ev.clientY - startY);

    const maxLeft = window.innerWidth - 40;
    const maxTop = window.innerHeight - 40;
    el.style.left = Math.max(-el.offsetWidth + 80, Math.min(maxLeft, left)) + offX + 'px';
    el.style.top = Math.max(0, Math.min(maxTop, top)) + offY + 'px';
  };

  const onUp = () => {
    if (!dragging) return;
    dragging = false;
    document.body.style.userSelect = '';
    report();
  };

  const onDown = ev => {
    dragging = true;
    const rect = el.getBoundingClientRect();

    ({ dx: offX, dy: offY } = pin(el, rect.left, rect.top));
    baseLeft = rect.left;
    baseTop = rect.top;
    startX = ev.clientX;
    startY = ev.clientY;
    document.body.style.userSelect = 'none';
    ev.preventDefault();
  };

  let sizes = null;
  if (sink && typeof ResizeObserver === 'function') {
    sizes = new ResizeObserver(() => {
      if (!dragging) report();
    });
    sizes.observe(el);
  }

  handle.addEventListener('pointerdown', onDown);
  window.addEventListener('pointermove', onMove);
  window.addEventListener('pointerup', onUp);

  return {
    dispose() {
      if (sizes) sizes.disconnect();
      handle.removeEventListener('pointerdown', onDown);
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerup', onUp);
    },
  };
}
