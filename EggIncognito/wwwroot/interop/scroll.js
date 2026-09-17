export function scrollToBottom(el) {
  if (el) el.scrollTop = el.scrollHeight;
}

export function centerOnFraction(el, fraction) {
  if (!el) return;
  const span = el.scrollWidth - el.clientWidth;
  if (span <= 0) return;
  const target = el.scrollWidth * fraction - el.clientWidth / 2;
  el.scrollLeft = Math.max(0, Math.min(span, target));
}
