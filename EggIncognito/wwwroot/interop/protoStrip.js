const EOCD_SIG = 0x06054b50;
const CEN_SIG = 0x02014b50;
const LOC_SIG = 0x04034b50;

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

function pickBundle(entries) {
  return entries.find(e => lower(e).endsWith(".apk") && lower(e).includes("arm64"))
    || entries.find(e => lower(e).endsWith(".apk")
      && (lower(e).includes("armeabi") || lower(e).includes("_v7a")))
    || null;
}

function pickAndroid(entries) {
  const preds = [
    e => lower(e).endsWith("/libegginc.so") && lower(e).includes("arm64"),
    e => lower(e).endsWith("/libegginc.so"),
    e => lower(e).includes("arm64") && lower(e).endsWith(".so"),
    e => lower(e).endsWith(".so")
  ];
  for (const p of preds) {
    const m = entries.find(p);
    if (m) return m;
  }
  return null;
}

function pickIos(entries) {
  const exec = entries.find(e => {
    const f = e.name;
    const fl = f.toLowerCase();
    if (!fl.startsWith("payload/")) return false;
    const i = fl.indexOf(".app/");
    if (i < 0) return false;
    const rest = f.slice(i + 5);
    return rest.length > 0 && !rest.includes("/") && !rest.includes(".");
  });
  if (exec) return exec;
  return entries.find(e => {
    const fl = e.name.toLowerCase();
    const base = e.name.split("/").pop();
    return fl.startsWith("payload/") && fl.includes(".framework/")
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

async function extractForUpload(blob) {
  const eocd = await findEocd(blob);
  if (!eocd) return { binary: blob, meta: null, metaName: null };
  const entries = await centralDir(blob, eocd);
  if (entries.length === 0) return { binary: blob, meta: null, metaName: null };

  const bundle = pickBundle(entries);
  if (bundle) {
    const inner = await entryBlob(blob, bundle);
    if (!inner) return { binary: blob, meta: null, metaName: null };
    const picked = await extractForUpload(inner);
    const baseE = entries.find(e => lower(e) === "base.apk" || lower(e).endsWith("/base.apk"))
      || entries.filter(e => lower(e).endsWith(".apk") && !lower(e).includes("config."))
        .sort((a, b) => b.compSize - a.compSize)[0]
      || null;
    if (baseE) {
      const baseBlob = await entryBlob(blob, baseE);
      const m = baseBlob ? await entryByName(baseBlob, "AndroidManifest.xml") : null;
      if (m) return { binary: picked.binary, meta: m, metaName: "AndroidManifest.xml" };
    }
    return picked;
  }

  const ios = pickIos(entries);
  if (ios) {
    const bin = await entryBlob(blob, ios);
    const plistE = entries.find(e => lower(e).startsWith("payload/") && lower(e).endsWith(".app/info.plist"));
    const meta = plistE ? await entryBlob(blob, plistE) : null;
    return { binary: bin || blob, meta, metaName: meta ? "Info.plist" : null };
  }

  const so = pickAndroid(entries);
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

function sizeText(bytes) {
  if (bytes >= 1048576) return (bytes / 1048576).toFixed(1) + " MB";
  return (bytes / 1024).toFixed(1) + " KB";
}

async function postForm(endpoint, form) {
  let res;
  try {
    res = await fetch(endpoint, {
      method: "POST",
      body: form,
      credentials: "same-origin",
      signal: AbortSignal.timeout(600000)
    });
  } catch (e) {
    return { error: "upload failed or timed out: " + String(e) };
  }
  let json = null;
  try {
    json = await res.json();
  } catch {
    json = null;
  }
  if (!res.ok) return { error: json?.error ? json.error : `HTTP ${res.status}` };
  return json ?? { error: "empty response" };
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

export async function analyzeStored(token, endpoint, dotnetRef) {
  const file = stash.get(token);
  if (!file) return { ok: false, diagnostics: "file no longer available" };
  try {
    await report(dotnetRef, token, "extracting binary from archive");
    const picked = await extractForUpload(file);
    const uploadedSize = picked.binary.size + (picked.meta ? picked.meta.size : 0);
    const form = new FormData();
    form.append("binary", picked.binary, "binary.bin");
    if (picked.meta) form.append("meta", picked.meta, picked.metaName || "meta.bin");
    form.append("fileName", file.name);
    await report(dotnetRef, token, `uploading (${sizeText(uploadedSize)})`);
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
