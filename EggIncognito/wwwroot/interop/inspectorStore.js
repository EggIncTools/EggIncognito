
import { get as getRaw, set as setRaw } from "./uiPrefs.js";

const SALT_KEY = "inspector.salt";
const RINFO_KEY = "inspector.rinfoDefaults";
const CUSTOM_TARGET_KEY = "inspector.customTarget";
const EIDS_KEY = "inspector.recentEids";
const LIVE_CONSENT_KEY = "egi:liveApiConsent";
const HISTORY_KEY = "inspector.history";
const HISTORY_MAX = 50;

const EID_RE = /^EI\d{10,}$/;
const EID_MAX = 12;
const RINFO_TEXT_KEYS = [
  "eiUserId",
  "clientVersion",
  "version",
  "build",
  "platform",
  "country",
  "language",
];

function lruUpsert(list, matches, entry, max) {
  const next = list.filter((e) => !matches(e));
  const order = next.reduce((m, e) => Math.max(m, e.order || 0), 0) + 1;
  next.push({ ...entry, order });
  next.sort((a, b) => (b.order || 0) - (a.order || 0));
  return next.slice(0, max);
}

export function getSalt() { return getRaw(SALT_KEY) || ""; }
export function setSalt(value) { setRaw(SALT_KEY, String(value ?? "")); }

export function getCustomTarget() { return getRaw(CUSTOM_TARGET_KEY) || ""; }
export function setCustomTarget(value) { setRaw(CUSTOM_TARGET_KEY, String(value ?? "").trim()); }

export function getLiveConsent() { return getRaw(LIVE_CONSENT_KEY) === "1"; }
export function setLiveConsent() { setRaw(LIVE_CONSENT_KEY, "1"); }
export function getRinfoDefaults() {
  const out = { debug: false };
  for (const k of RINFO_TEXT_KEYS) out[k] = "";
  try {
    const saved = JSON.parse(getRaw(RINFO_KEY) || "{}");
    if (saved && typeof saved === "object") {
      for (const k of RINFO_TEXT_KEYS) {
        if (saved[k] !== undefined && saved[k] !== null) out[k] = String(saved[k]);
      }
      out.debug = saved.debug === true;
    }
  } catch {
    return out;
  }
  return out;
}
export function setRinfoDefaults(obj) { setRaw(RINFO_KEY, JSON.stringify(obj || {})); }
function loadEids() {
  try {
    const raw = JSON.parse(getRaw(EIDS_KEY) || "[]");
    return Array.isArray(raw) ? raw.filter((e) => e && EID_RE.test(e.eid)) : [];
  } catch { return []; }
}
export function rememberEid(value) {
  const eid = String(value || "").trim();
  if (EID_RE.test(eid)) {
    setRaw(EIDS_KEY, JSON.stringify(lruUpsert(loadEids(), (e) => e.eid === eid, { eid }, EID_MAX)));
  }
  return recentEids();
}
export function rememberEids(values) {
  let list = loadEids();
  let changed = false;
  for (const v of values || []) {
    const eid = String(v || "").trim();
    if (!EID_RE.test(eid)) continue;
    list = lruUpsert(list, (e) => e.eid === eid, { eid }, EID_MAX);
    changed = true;
  }
  if (changed) setRaw(EIDS_KEY, JSON.stringify(list));
  return recentEids();
}

export function recentEids() {
  return loadEids().sort((a, b) => b.order - a.order).map((e) => e.eid);
}
export function forgetEids() {
  setRaw(EIDS_KEY, "[]");
  return [];
}

function loadHistory() {
  try {
    const raw = JSON.parse(getRaw(HISTORY_KEY) || "[]");
    return Array.isArray(raw) ? raw : [];
  } catch { return []; }
}

export function getHistory() {
  return loadHistory().sort((a, b) => (b.order || 0) - (a.order || 0));
}

export function saveHistory(entry) {
  if (!entry || !entry.path) return getHistory();
  const list = lruUpsert(
    loadHistory(),
    (e) => e.path === entry.path && e.fieldsJson === entry.fieldsJson && (e.pathParam || "") === (entry.pathParam || ""),
    entry,
    HISTORY_MAX);
  setRaw(HISTORY_KEY, JSON.stringify(list));
  return getHistory();
}

export function deleteHistory(id) {
  setRaw(HISTORY_KEY, JSON.stringify(loadHistory().filter((e) => e.id !== id)));
  return getHistory();
}
export function clearHistory() {
  setRaw(HISTORY_KEY, "[]");
  return [];
}

export async function postForm(url, formBody) {
  const resp = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: formBody,
  });
  return (await resp.text()).trim();
}
