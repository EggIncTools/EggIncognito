import { formatBytes, postMultipart } from "./http.js";

const EOCD_SIG = 0x06054b50;
const CEN_SIG = 0x02014b50;
const LOC_SIG = 0x04034b50;

const ARCHIVE_RULES_URL = "/api/tools/archive-rules";

let archiveRulesPromise = null;

async function fetchArchiveRules() {
  const res = await fetch(ARCHIVE_RULES_URL, { credentials: "same-origin" });
  if (!res.ok) throw new Error(`failed to load archive rules: HTTP ${res.status}`);
  const rules = await res.json();
  const required = ["nativeLibNames", "preferredAbis", "splitPatterns", "baseModuleName", "iosExecutableHints", "plistNames"];
  for (const key of required) {
    if (rules[key] === undefined) throw new Error(`archive rules missing field: ${key}`);
  }
  return rules;
}

function loadArchiveRules() {
  if (!archiveRulesPromise) {
    archiveRulesPromise = fetchArchiveRules().catch(e => {
      archiveRulesPromise = null;
      throw e;
    });
  }
  return archiveRulesPromise;
}

loadArchiveRules().catch(() => {});

async function bytes(blob, start, end) {
  return new Uint8Array(await blob.slice(start, end).arrayBuffer());
}

async function findEocd(blob) {
  const size = blob.size;
  if (size < 22) return null;
  const back = Math.min(size, 65557);
  const buf = await bytes(blob, size - back, size);
  const dv = new DataView(buf.buffer);
  for (let i = buf.length - 22; i >= 0; i--) {
    if (dv.getUint32(i, true) === EOCD_SIG) {
      const count = dv.getUint16(i + 10, true);
      const cdSize = dv.getUint32(i + 12, true);
      const cdOffset = dv.getUint32(i + 16, true);
      if (cdOffset === 0xffffffff || count === 0xffff || cdSize === 0xffffffff) return null;
      return { cdOffset, cdSize };
    }
  }
  return null;
}

async function centralDir(blob, eocd) {
  const buf = await bytes(blob, eocd.cdOffset, eocd.cdOffset + eocd.cdSize);
  const dv = new DataView(buf.buffer);
  const dec = new TextDecoder();
  const entries = [];
  let o = 0;
  while (o + 46 <= buf.length && dv.getUint32(o, true) === CEN_SIG) {
    const method = dv.getUint16(o + 10, true);
    const compSize = dv.getUint32(o + 20, true);
    const nameLen = dv.getUint16(o + 28, true);
    const extraLen = dv.getUint16(o + 30, true);
    const commentLen = dv.getUint16(o + 32, true);
    const localOffset = dv.getUint32(o + 42, true);
    const name = dec.decode(buf.subarray(o + 46, o + 46 + nameLen));
    entries.push({ name, method, compSize, localOffset });
    o += 46 + nameLen + extraLen + commentLen;
  }
  return entries;
}

async function entryBlob(blob, entry) {
  const lh = await bytes(blob, entry.localOffset, entry.localOffset + 30);
  const dv = new DataView(lh.buffer);
  if (dv.getUint32(0, true) !== LOC_SIG) return null;
  const nameLen = dv.getUint16(26, true);
  const extraLen = dv.getUint16(28, true);
  const dataStart = entry.localOffset + 30 + nameLen + extraLen;
  const comp = blob.slice(dataStart, dataStart + entry.compSize);
  if (entry.method === 0) return comp;
  if (entry.method === 8) {
    if (typeof DecompressionStream === "undefined") return null;
    const stream = comp.stream().pipeThrough(new DecompressionStream("deflate-raw"));
    return await new Response(stream).blob();
  }
  return null;
}

function lower(e) {
  return e.name.toLowerCase();
}

function soSuffixOf(nativeLibNames) {
  const name = nativeLibNames[0] || "";
  const i = name.lastIndexOf(".");
  return i >= 0 ? name.slice(i) : "";
}

function pickBundle(entries, rules) {
  const apkSuffix = rules.splitPatterns[0].toLowerCase();
  const [primaryAbi, ...fallbackAbis] = rules.preferredAbis.map(a => a.toLowerCase());
  return entries.find(e => lower(e).endsWith(apkSuffix) && lower(e).includes(primaryAbi))
    || entries.find(e => lower(e).endsWith(apkSuffix) && fallbackAbis.some(a => lower(e).includes(a)))
    || null;
}

function pickAndroid(entries, rules) {
  const libNames = rules.nativeLibNames.map(n => n.toLowerCase());
  const primaryAbi = rules.preferredAbis[0].toLowerCase();
  const soSuffix = soSuffixOf(libNames);
  const preds = [
    ...libNames.map(lib => e => lower(e).endsWith(`/${lib}`) && lower(e).includes(primaryAbi)),
    ...libNames.map(lib => e => lower(e).endsWith(`/${lib}`)),
    ...(soSuffix ? [e => lower(e).includes(primaryAbi) && lower(e).endsWith(soSuffix)] : []),
    ...(soSuffix ? [e => lower(e).endsWith(soSuffix)] : [])
  ];
  for (const p of preds) {
    const m = entries.find(p);
    if (m) return m;
  }
  return null;
}

function pickIos(entries, rules) {
  const [payloadPrefix, appDirMarker, frameworkDirMarker] = rules.iosExecutableHints.map(h => h.toLowerCase());
  const exec = entries.find(e => {
    const f = e.name;
    const fl = f.toLowerCase();
    if (!fl.startsWith(payloadPrefix)) return false;
    const i = fl.indexOf(appDirMarker);
    if (i < 0) return false;
    const rest = f.slice(i + appDirMarker.length);
    return rest.length > 0 && !rest.includes("/") && !rest.includes(".");
  });
  if (exec) return exec;
  return entries.find(e => {
    const fl = e.name.toLowerCase();
    const base = e.name.split("/").pop();
    return fl.startsWith(payloadPrefix) && fl.includes(frameworkDirMarker)
      && !fl.endsWith("/") && !base.includes(".");
  }) || null;
}

async function entryByName(blob, name) {
  const eocd = await findEocd(blob);
  if (!eocd) return null;
  const entries = await centralDir(blob, eocd);
  const e = entries.find(x => x.name === name);
  return e ? await entryBlob(blob, e) : null;
}

async function extractForUpload(blob, rules) {
  const eocd = await findEocd(blob);
  if (!eocd) return { binary: blob, meta: null, metaName: null };
  const entries = await centralDir(blob, eocd);
  if (entries.length === 0) return { binary: blob, meta: null, metaName: null };

  const bundle = pickBundle(entries, rules);
  if (bundle) {
    const inner = await entryBlob(blob, bundle);
    if (!inner) return { binary: blob, meta: null, metaName: null };
    const picked = await extractForUpload(inner, rules);
    const baseModuleName = rules.baseModuleName.toLowerCase();
    const apkSuffix = rules.splitPatterns[0].toLowerCase();
    const configSplitPrefix = rules.splitPatterns[1]?.toLowerCase();
    const baseE = entries.find(e => lower(e) === baseModuleName || lower(e).endsWith(`/${baseModuleName}`))
      || entries.filter(e => lower(e).endsWith(apkSuffix)
        && !(configSplitPrefix && lower(e).includes(configSplitPrefix)))
        .sort((a, b) => b.compSize - a.compSize)[0]
      || null;
    if (baseE) {
      const baseBlob = await entryBlob(blob, baseE);
      const m = baseBlob ? await entryByName(baseBlob, "AndroidManifest.xml") : null;
      if (m) return { binary: picked.binary, meta: m, metaName: "AndroidManifest.xml" };
    }
    return picked;
  }

  const ios = pickIos(entries, rules);
  if (ios) {
    const bin = await entryBlob(blob, ios);
    const payloadPrefix = rules.iosExecutableHints[0].toLowerCase();
    const appDirMarker = rules.iosExecutableHints[1].toLowerCase();
    const plistName = rules.plistNames[0];
    const plistSuffix = `${appDirMarker}${plistName.toLowerCase()}`;
    const plistE = entries.find(e => lower(e).startsWith(payloadPrefix) && lower(e).endsWith(plistSuffix));
    const meta = plistE ? await entryBlob(blob, plistE) : null;
    return { binary: bin || blob, meta, metaName: meta ? plistName : null };
  }

  const so = pickAndroid(entries, rules);
  if (so) {
    const bin = await entryBlob(blob, so);
    const manE = entries.find(e => e.name === "AndroidManifest.xml");
    const meta = manE ? await entryBlob(blob, manE) : null;
    return { binary: bin || blob, meta, metaName: meta ? "AndroidManifest.xml" : null };
  }

  return { binary: blob, meta: null, metaName: null };
}

async function report(dotnetRef, token, msg) {
  if (!dotnetRef) return;
  try { await dotnetRef.invokeMethodAsync("AnalyzeStep", token, msg); } catch {}
}

async function postForm(endpoint, form) {
  const result = await postMultipart(endpoint, form);
  if (!result.ok) return { error: typeof result.body === "string" ? result.body : `HTTP ${result.status}` };
  return result.body ?? { error: "empty response" };
}

const stash = new Map();
let nextToken = 1;

export function stashFiles(inputId) {
  const el = document.getElementById(inputId);
  const out = [];
  for (const f of el?.files ?? []) {
    const token = nextToken++;
    stash.set(token, f);
    out.push({ token, name: f.name, size: f.size });
  }
  if (el) el.value = "";
  return out;
}

export function discard(token) {
  stash.delete(token);
}

async function sha256Hex(blob) {
  if (!globalThis.crypto?.subtle) return null;
  const digest = await crypto.subtle.digest("SHA-256", await blob.arrayBuffer());
  return Array.from(new Uint8Array(digest), b => b.toString(16).padStart(2, "0")).join("");
}

async function knownResult(endpoint, fileSha, picked) {
  if (!fileSha) return null;
  try {
    const form = new FormData();
    if (picked.meta) form.append("meta", picked.meta, picked.metaName || "meta.bin");
    const res = await fetch(`${endpoint}/known/${fileSha}`, { method: "POST", body: form, credentials: "same-origin" });
    if (!res.ok) return null;
    const json = await res.json();
    return json?.ok ? json : null;
  } catch {
    return null;
  }
}

export async function analyzeStored(token, endpoint, dotnetRef) {
  const file = stash.get(token);
  if (!file) return { ok: false, diagnostics: "file no longer available" };
  try {
    const rules = await loadArchiveRules();
    await report(dotnetRef, token, "extracting binary from archive");
    const picked = await extractForUpload(file, rules);
    await report(dotnetRef, token, "hashing binary");
    const known = await knownResult(endpoint, await sha256Hex(picked.binary), picked);
    if (known) return { ...known, fileName: file.name };
    const uploadedSize = picked.binary.size + (picked.meta ? picked.meta.size : 0);
    const form = new FormData();
    form.append("binary", picked.binary, "binary.bin");
    if (picked.meta) form.append("meta", picked.meta, picked.metaName || "meta.bin");
    form.append("fileName", file.name);
    await report(dotnetRef, token, `uploading (${formatBytes(uploadedSize)})`);
    const r = await postForm(endpoint, form);
    if (r?.error) return { ok: false, diagnostics: r.error };
    return { ...r, fileName: file.name };
  } catch (e) {
    return { ok: false, diagnostics: String(e) };
  }
}

export function wireDrop(containerId, inputId) {
  const el = document.getElementById(containerId);
  const input = document.getElementById(inputId);
  if (!el || !input) return false;
  const stop = e => {
    e.preventDefault();
    e.stopPropagation();
  };
  el.addEventListener("dragover", e => {
    stop(e);
    el.classList.add("dragging");
  });
  el.addEventListener("dragleave", e => {
    stop(e);
    el.classList.remove("dragging");
  });
  el.addEventListener("drop", e => {
    stop(e);
    el.classList.remove("dragging");
    if (!e.dataTransfer?.files?.length) return;
    input.files = e.dataTransfer.files;
    input.dispatchEvent(new Event("change", { bubbles: true }));
  });
  return true;
}

export function wireRegistryClip(clipId, sideSelector, narrowRows) {
  const clip = document.getElementById(clipId);
  const more = clip?.querySelector(".reg-more");
  if (!clip || !more || clip.dataset.wired) return false;
  clip.dataset.wired = "1";
  const side = clip.closest(".pd-grid")?.querySelector(sideSelector);
  const wide = matchMedia("(min-width: 90rem)");

  const fadePx = () => {
    const rem = parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
    return (parseFloat(getComputedStyle(clip).getPropertyValue("--reg-fade")) || 0) * rem;
  };
  const tables = () => [...clip.querySelectorAll(".reg-table")].filter(t => t.offsetParent !== null);
  const bandsOf = t => t.querySelectorAll("tbody.reg-band");
  const reset = () => {
    clip.style.removeProperty("max-height");
    delete clip.dataset.more;
    more.textContent = "";
  };

  const limitOf = (top, fade) => {
    if (wide.matches) return side ? side.getBoundingClientRect().bottom - top - 1 : null;
    let bottom = 0;
    for (const t of tables()) {
      const bands = bandsOf(t);
      const last = bands[Math.min(bands.length, narrowRows) - 1];
      if (last) bottom = Math.max(bottom, last.getBoundingClientRect().bottom - top + fade);
    }
    return bottom || null;
  };

  const update = () => {
    if (clip.dataset.open) return reset();
    const top = clip.getBoundingClientRect().top;
    const fade = fadePx();
    const raw = limitOf(top, fade);
    if (raw === null) return reset();
    const limit = Math.max(raw, fade * 2);
    const content = Math.max(0, ...[...clip.children].filter(c => c !== more).map(c => c.getBoundingClientRect().bottom - top));
    if (content <= limit + 1) return reset();
    const cutoff = top + limit - fade - 1;
    let hidden = 0;
    for (const t of tables()) {
      for (const b of bandsOf(t)) if (b.getBoundingClientRect().top >= cutoff) hidden++;
    }
    if (hidden === 0) return reset();
    clip.style.maxHeight = `${limit}px`;
    clip.dataset.more = "1";
    more.textContent = `+ ${hidden} more`;
  };

  let queued = false;
  const schedule = () => {
    if (queued) return;
    queued = true;
    requestAnimationFrame(() => {
      queued = false;
      update();
    });
  };

  const sizes = new ResizeObserver(schedule);
  const observeChildren = () => {
    for (const c of clip.children) if (c !== more) sizes.observe(c);
  };
  if (side) sizes.observe(side);
  observeChildren();
  new MutationObserver(() => {
    observeChildren();
    schedule();
  }).observe(clip, { childList: true, subtree: true });
  wide.addEventListener("change", schedule);
  more.addEventListener("click", () => {
    clip.dataset.open = "1";
    update();
  });
  schedule();
  return true;
}
