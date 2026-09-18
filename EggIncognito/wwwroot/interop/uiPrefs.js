export function get(key) {
  try { return localStorage.getItem(key); } catch { return null; }
}
export function set(key, val) {
  try { localStorage.setItem(key, val); } catch { }
}
export function remove(key) {
  try { localStorage.removeItem(key); } catch { }
}
