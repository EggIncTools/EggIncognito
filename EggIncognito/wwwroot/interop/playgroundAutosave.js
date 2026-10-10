function get(key) {
  try { return localStorage.getItem(key); } catch { return null; }
}
function set(key, val) {
  try { localStorage.setItem(key, val); } catch { }
}
function remove(key) {
  try { localStorage.removeItem(key); } catch { }
}

const KEY = 'playground.autosave';

export function save(json, version, savedAtMs) {
  set(KEY, JSON.stringify({ json, version: version || '', savedAt: savedAtMs || 0 }));
}

export function load() {
  try {
    const raw = get(KEY);
    if (!raw) return null;
    const rec = JSON.parse(raw);
    if (!rec || typeof rec.json !== 'string') return null;
    return rec;
  } catch {
    return null;
  }
}

export function clear() {
  remove(KEY);
}
