const GAP = 6;
const EDGE = 8;
const HIDE_DELAY = 120;

function popOf(anchor) {
  return anchor.querySelector(':scope > .evcal-pop');
}

function locked(anchor) {
  return anchor.classList.contains('evcal-anchor-open');
}

function place(anchor) {
  const pop = popOf(anchor);
  if (!pop) return;
  pop.style.display = 'block';
  const r = anchor.getBoundingClientRect();
  const w = pop.offsetWidth, h = pop.offsetHeight;
  const left = Math.max(EDGE, Math.min(r.left, window.innerWidth - w - EDGE));
  let top = r.bottom + GAP;
  if (top + h > window.innerHeight - EDGE) top = r.top - h - GAP;
  if (top < EDGE) top = EDGE;
  pop.style.left = left + 'px';
  pop.style.top = top + 'px';
}

function hide(anchor) {
  if (locked(anchor)) return;
  const pop = popOf(anchor);
  if (pop) pop.style.display = '';
}

export function attach(root) {
  if (!root) return { dispose() {} };
  let hover = null;
  let timer = 0;

  const over = ev => {
    const anchor = ev.target.closest?.('.evcal-anchor');
    if (!anchor || !root.contains(anchor)) return;
    clearTimeout(timer);
    if (anchor === hover) return;
    if (hover) hide(hover);
    hover = anchor;
    place(anchor);
  };

  const out = ev => {
    const anchor = ev.target.closest?.('.evcal-anchor');
    if (!anchor || anchor !== hover) return;
    if (ev.relatedTarget && anchor.contains(ev.relatedTarget)) return;
    clearTimeout(timer);
    timer = setTimeout(() => {
      if (hover !== anchor) return;
      hide(anchor);
      hover = null;
    }, HIDE_DELAY);
  };

  const reflow = () => {
    for (const anchor of root.querySelectorAll('.evcal-anchor-open')) place(anchor);
    if (hover && root.contains(hover)) place(hover);
    else hover = null;
  };

  root.addEventListener('pointerover', over);
  root.addEventListener('pointerout', out);
  root.addEventListener('scroll', reflow, true);
  window.addEventListener('resize', reflow);
  const classes = new MutationObserver(reflow);
  classes.observe(root, { subtree: true, childList: true, attributes: true, attributeFilter: ['class'] });

  return {
    dispose() {
      clearTimeout(timer);
      classes.disconnect();
      root.removeEventListener('pointerover', over);
      root.removeEventListener('pointerout', out);
      root.removeEventListener('scroll', reflow, true);
      window.removeEventListener('resize', reflow);
    },
  };
}
