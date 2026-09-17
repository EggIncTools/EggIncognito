const sessions = new Map();
const FIRST_FRAME_MS = 8000;
const STALL_MS = 8000;
const VIDEO_STALL_MS = 30000;

function detach(img) {
  const s = sessions.get(img);
  if (!s) return null;
  img.removeEventListener("load", s.onLoad);
  img.removeEventListener("error", s.onError);
  if (s.statsTimer) {
    clearInterval(s.statsTimer);
    s.statsTimer = 0;
  }
  sessions.delete(img);
  return s;
}

function halt(img) {
  if (!img) return;
  detach(img);
  img.removeAttribute("src");
}

function report(img, s) {
  const w = img.naturalWidth;
  const h = img.naturalHeight;
  if (w === s.lastW && h === s.lastH) return;
  s.lastW = w;
  s.lastH = h;
  s.dotnet.invokeMethodAsync("OnFrameSize", w, h);
}

function invokeQuiet(dotnet, method, ...args) {
  try {
    const p = dotnet.invokeMethodAsync(method, ...args);
    if (p && typeof p.catch === "function") p.catch(() => {});
  } catch {
  }
}

function errText(e) {
  return e?.message ?? String(e);
}

function describeHttp(status, body) {
  let error = "";
  try {
    const parsed = JSON.parse(body);
    if (parsed && typeof parsed.error === "string") error = parsed.error;
  } catch {
  }
  if (!error && body) error = String(body).slice(0, 160);
  return error ? "http " + status + ": " + error : "http " + status;
}

async function explainImageFailure(screenshotUrl, fallback) {
  if (!screenshotUrl) return { status: 0, note: fallback };
  try {
    const response = await fetch(bust(screenshotUrl), { cache: "no-store", credentials: "same-origin" });
    if (response.ok) return { status: response.status, note: fallback + "; the device still answers screenshots" };
    const body = await response.text();
    return { status: response.status, note: describeHttp(response.status, body) };
  } catch (e) {
    return { status: 0, note: fallback + "; screenshot fetch failed: " + errText(e) };
  }
}

function failImage(img, s, fallback) {
  detach(img);
  img.removeAttribute("src");
  explainImageFailure(s.screenshotUrl, fallback).then((r) => invokeQuiet(s.dotnet, "OnFrameFailed", r.status, r.note));
}

function attach(img, dotnet, screenshotUrl, note, once) {
  const s = { dotnet, screenshotUrl, lastW: -1, lastH: -1, frames: 0, statsTimer: 0, lastFrameAt: 0, startedAt: performance.now() };
  s.onLoad = () => {
    if (once) detach(img);
    s.frames++;
    s.lastFrameAt = performance.now();
    report(img, s);
  };
  s.onError = () => failImage(img, s, note);
  img.addEventListener("load", s.onLoad);
  img.addEventListener("error", s.onError);
  sessions.set(img, s);
  return s;
}

function bust(url) {
  return url + (url.indexOf("?") >= 0 ? "&" : "?") + "t=" + Date.now();
}

export function start(img, streamUrl, screenshotUrl, dotnet) {
  if (!img) return false;
  halt(img);
  const s = attach(img, dotnet, screenshotUrl, "the device stopped sending frames", false);
  s.statsAt = performance.now();
  s.statsTimer = setInterval(() => {
    const now = performance.now();
    if (s.lastFrameAt === 0 && now - s.startedAt > FIRST_FRAME_MS) {
      failImage(img, s, "no first frame within " + Math.round(FIRST_FRAME_MS / 1000) + "s");
      return;
    }
    if (s.lastFrameAt > 0 && now - s.lastFrameAt > STALL_MS) {
      failImage(img, s, "no frames for " + Math.round(STALL_MS / 1000) + "s");
      return;
    }
    const dt = Math.max(1, now - s.statsAt);
    const fps = Math.round(((s.frames * 1000) / dt) * 10) / 10;
    s.statsAt = now;
    s.frames = 0;
    invokeQuiet(dotnet, "OnStreamFps", fps);
  }, 1000);
  img.src = bust(streamUrl);
  return true;
}

export function stop(img) {
  halt(img);
}

export function once(img, url, dotnet) {
  if (!img) return false;
  halt(img);
  attach(img, dotnet, null, "no frame came back", true);
  img.src = bust(url);
  return true;
}

export function measure(img) {
  if (!img) return [0, 0, 0, 0];
  return [img.naturalWidth, img.naturalHeight, img.clientWidth, img.clientHeight];
}

export function clear(img) {
  halt(img);
}

const videoSessions = new WeakMap();
const liveVideos = new Map();
const watchSessions = new WeakMap();
const WATCH_MIN_MS = 100;
const CHUNK_US = 33333;
const PENDING_CAP = 64;
const DECODER_ERROR_LIMIT = 5;
let videoSeq = 0;

export function supportsVideo() {
  return typeof window !== "undefined" && typeof window.VideoDecoder === "function" && typeof window.EncodedVideoChunk === "function";
}

export function splitNals(bytes) {
  const nals = [];
  const n = bytes.length;
  let payloadStart = -1;
  let lastCode = -1;
  let i = 0;
  while (i + 2 < n) {
    if (bytes[i] === 0 && bytes[i + 1] === 0 && bytes[i + 2] === 1) {
      if (payloadStart >= 0) {
        let end = i;
        while (end > payloadStart && bytes[end - 1] === 0) end--;
        if (end > payloadStart) nals.push(bytes.slice(payloadStart, end));
      }
      lastCode = i;
      payloadStart = i + 3;
      i += 3;
      continue;
    }
    i++;
  }
  const rest = lastCode >= 0 ? bytes.slice(lastCode) : bytes.slice(0);
  return { nals, rest };
}

function concatBytes(a, b) {
  if (!a || a.length === 0) return b;
  if (!b || b.length === 0) return a;
  const out = new Uint8Array(a.length + b.length);
  out.set(a, 0);
  out.set(b, a.length);
  return out;
}

function annexB(parts) {
  let len = 0;
  for (const p of parts) len += 4 + p.length;
  const out = new Uint8Array(len);
  let o = 0;
  for (const p of parts) {
    out[o + 3] = 1;
    o += 4;
    out.set(p, o);
    o += p.length;
  }
  return out;
}

function unescapeRbsp(nal) {
  const out = [];
  let zeros = 0;
  for (let i = 0; i < nal.length; i++) {
    const b = nal[i];
    if (zeros >= 2 && b === 3) {
      zeros = 0;
      continue;
    }
    out.push(b);
    zeros = b === 0 ? zeros + 1 : 0;
  }
  return Uint8Array.from(out);
}

class BitReader {
  constructor(bytes) {
    this.b = bytes;
    this.pos = 0;
  }

  bit() {
    const i = this.pos >> 3;
    if (i >= this.b.length) throw new RangeError("sps truncated");
    const v = (this.b[i] >> (7 - (this.pos & 7))) & 1;
    this.pos++;
    return v;
  }

  bits(n) {
    let v = 0;
    for (let k = 0; k < n; k++) v = v * 2 + this.bit();
    return v;
  }

  ue() {
    let zeros = 0;
    while (this.bit() === 0) {
      zeros++;
      if (zeros > 31) throw new RangeError("bad exp-golomb");
    }
    return zeros === 0 ? 0 : 2 ** zeros - 1 + this.bits(zeros);
  }

  se() {
    const k = this.ue();
    return k & 1 ? (k + 1) / 2 : -(k / 2);
  }
}

function skipScalingList(br, size) {
  let last = 8;
  let next = 8;
  for (let j = 0; j < size; j++) {
    if (next !== 0) next = (last + br.se() + 256) % 256;
    last = next === 0 ? last : next;
  }
}

const HIGH_PROFILES = new Set([100, 110, 122, 244, 44, 83, 86, 118, 128, 138, 139, 134, 135]);

function hex2(v) {
  return v.toString(16).toUpperCase().padStart(2, "0");
}

function parseSps(nal) {
  const rbsp = unescapeRbsp(nal.subarray(1));
  const br = new BitReader(rbsp);
  const profileIdc = br.bits(8);
  const constraints = br.bits(8);
  const levelIdc = br.bits(8);
  br.ue();
  let chromaFormatIdc = 1;
  let separateColourPlane = 0;
  if (HIGH_PROFILES.has(profileIdc)) {
    chromaFormatIdc = br.ue();
    if (chromaFormatIdc === 3) separateColourPlane = br.bit();
    br.ue();
    br.ue();
    br.bit();
    if (br.bit()) {
      const count = chromaFormatIdc !== 3 ? 8 : 12;
      for (let i = 0; i < count; i++) {
        if (br.bit()) skipScalingList(br, i < 6 ? 16 : 64);
      }
    }
  }
  br.ue();
  const pocType = br.ue();
  if (pocType === 0) {
    br.ue();
  } else if (pocType === 1) {
    br.bit();
    br.se();
    br.se();
    const cycle = br.ue();
    for (let i = 0; i < cycle; i++) br.se();
  }
  br.ue();
  br.bit();
  const widthMbs = br.ue() + 1;
  const heightMapUnits = br.ue() + 1;
  const frameMbsOnly = br.bit();
  if (!frameMbsOnly) br.bit();
  br.bit();
  let cropLeft = 0;
  let cropRight = 0;
  let cropTop = 0;
  let cropBottom = 0;
  if (br.bit()) {
    cropLeft = br.ue();
    cropRight = br.ue();
    cropTop = br.ue();
    cropBottom = br.ue();
  }
  let subW = 1;
  let subH = 1;
  if (!separateColourPlane) {
    if (chromaFormatIdc === 1) {
      subW = 2;
      subH = 2;
    } else if (chromaFormatIdc === 2) {
      subW = 2;
      subH = 1;
    }
  }
  const cropUnitX = subW;
  const cropUnitY = subH * (2 - frameMbsOnly);
  const width = widthMbs * 16 - (cropLeft + cropRight) * cropUnitX;
  const height = (2 - frameMbsOnly) * heightMapUnits * 16 - (cropTop + cropBottom) * cropUnitY;
  return { codec: "avc1." + hex2(profileIdc) + hex2(constraints) + hex2(levelIdc), width, height };
}

function safeInvoke(s, method, ...args) {
  if (!s.dotnet) return;
  try {
    const p = s.dotnet.invokeMethodAsync(method, ...args);
    if (p && typeof p.catch === "function") p.catch(() => {});
  } catch {
  }
}

function closeDecoder(s) {
  const d = s.decoder;
  s.decoder = null;
  if (!d) return;
  try {
    if (d.state !== "closed") d.close();
  } catch {
  }
}

function blank(canvas) {
  if (!canvas) return;
  const ctx = canvas.getContext("2d");
  if (!ctx) return;
  ctx.fillStyle = "#000";
  ctx.fillRect(0, 0, canvas.width, canvas.height);
}

function finish(s, reason, status, retryable) {
  if (s.ended) return;
  s.ended = true;
  s.stopped = true;
  if (s.statsTimer) {
    clearInterval(s.statsTimer);
    s.statsTimer = 0;
  }
  try {
    s.ctrl.abort();
  } catch {
  }
  if (s.reader) {
    const r = s.reader;
    s.reader = null;
    try {
      const p = r.cancel();
      if (p && typeof p.catch === "function") p.catch(() => {});
    } catch {
    }
  }
  closeDecoder(s);
  s.pending.clear();
  s.scratch = null;
  s.scratchCtx = null;
  if (videoSessions.get(s.canvas) === s) videoSessions.delete(s.canvas);
  if (liveVideos.get(s.key) === s) liveVideos.delete(s.key);
  if (s.canvas && s.canvas.isConnected) blank(s.canvas);
  safeInvoke(s, "OnVideoEnded", s.token, reason, status || 0, retryable === true);
}

function sweepOrphans(keep) {
  for (const s of [...liveVideos.values()]) {
    if (s === keep || s.ended) continue;
    if (!s.canvas || s.canvas.isConnected) continue;
    finish(s, "the canvas was replaced", 0, true);
  }
}

function scratchFor(s, w, h) {
  if (!s.scratch) {
    s.scratch = document.createElement("canvas");
    s.scratch.width = Math.max(1, w);
    s.scratch.height = Math.max(1, h);
    s.scratchCtx = s.scratch.getContext("2d", { alpha: false, willReadFrequently: true });
  } else if (s.scratch.width < w || s.scratch.height < h) {
    s.scratch.width = Math.max(s.scratch.width, w);
    s.scratch.height = Math.max(s.scratch.height, h);
  }
  return s.scratchCtx;
}

function readPixel(s, fx, fy) {
  const w = s.canvas.width;
  const h = s.canvas.height;
  if (w <= 0 || h <= 0) return null;
  if (!(fx >= 0) || fx > 1 || !(fy >= 0) || fy > 1) return null;
  const px = Math.min(w - 1, Math.floor(fx * w));
  const py = Math.min(h - 1, Math.floor(fy * h));
  const ctx = scratchFor(s, 1, 1);
  if (!ctx) return null;
  try {
    ctx.drawImage(s.canvas, px, py, 1, 1, 0, 0, 1, 1);
    const d = ctx.getImageData(0, 0, 1, 1).data;
    return [d[0], d[1], d[2]];
  } catch {
    return null;
  }
}

function near(a, b, tolerance) {
  return Math.abs(a - b) <= tolerance;
}

function checkWatch(s, frame, fw, fh, now) {
  const w = watchSessions.get(s.canvas);
  if (!w || w.points.length === 0) return;
  if (now - w.checkedAt < WATCH_MIN_MS) return;
  w.checkedAt = now;
  const due = [];
  for (const p of w.points) {
    if (now < (w.cooldowns.get(p.id) || 0)) continue;
    if (!(p.fx >= 0) || p.fx > 1 || !(p.fy >= 0) || p.fy > 1) continue;
    due.push(p);
  }
  if (due.length === 0) return;
  const ctx = scratchFor(s, due.length, 1);
  if (!ctx) return;
  let data;
  try {
    for (let i = 0; i < due.length; i++) {
      const sx = Math.min(fw - 1, Math.floor(due[i].fx * fw));
      const sy = Math.min(fh - 1, Math.floor(due[i].fy * fh));
      ctx.drawImage(frame, sx, sy, 1, 1, i, 0, 1, 1);
    }
    data = ctx.getImageData(0, 0, due.length, 1).data;
  } catch {
    return;
  }
  for (let i = 0; i < due.length; i++) {
    const p = due[i];
    const o = i * 4;
    if (!near(data[o], p.r, w.tolerance) || !near(data[o + 1], p.g, w.tolerance) || !near(data[o + 2], p.b, w.tolerance)) continue;
    w.cooldowns.set(p.id, now + w.cooldownMs);
    safeInvoke(s, "OnWatchHit", p.id);
  }
}

function paint(s, frame, arrival) {
  const w = frame.displayWidth || frame.codedWidth;
  const h = frame.displayHeight || frame.codedHeight;
  if (w <= 0 || h <= 0) return;
  try {
    if (s.canvas.width !== w || s.canvas.height !== h) {
      s.canvas.width = w;
      s.canvas.height = h;
    }
    s.ctx.drawImage(frame, 0, 0, w, h);
  } catch {
    return;
  }
  s.frameW = w;
  s.frameH = h;
  s.drawn++;
  const now = performance.now();
  s.lastFrameAt = now;
  if (arrival >= 0) {
    s.latencySum += now - arrival;
    s.latencyCount++;
  }
  checkWatch(s, frame, w, h, now);
}

function onFrame(s, frame) {
  if (s.ended) {
    try {
      frame.close();
    } catch {
    }
    return;
  }
  s.decoded++;
  const arrival = s.pending.get(frame.timestamp);
  s.pending.delete(frame.timestamp);
  try {
    paint(s, frame, arrival === undefined ? -1 : arrival);
  } finally {
    try {
      frame.close();
    } catch {
    }
  }
}

function queueDepth(s) {
  const d = s.decoder;
  return d && d.state === "configured" ? d.decodeQueueSize : 0;
}

function tickStats(s) {
  if (s.ended) return;
  if (s.canvas && !s.canvas.isConnected) {
    finish(s, "the canvas was replaced", 0, true);
    return;
  }
  const now = performance.now();
  if (s.lastFrameAt === 0 && now - s.startedAt > FIRST_FRAME_MS) {
    finish(s, "no first frame within " + Math.round(FIRST_FRAME_MS / 1000) + "s", 0, false);
    return;
  }
  if (s.lastFrameAt > 0 && now - s.lastFrameAt > VIDEO_STALL_MS) {
    finish(s, "no frames for " + Math.round(VIDEO_STALL_MS / 1000) + "s", 0, true);
    return;
  }
  const dt = Math.max(1, now - s.statsAt);
  const stats = {
    token: s.token,
    fps: Math.round((s.drawn * 1000) / dt),
    latencyMs: s.latencyCount > 0 ? Math.round(s.latencySum / s.latencyCount) : 0,
    frameW: s.frameW,
    frameH: s.frameH,
    kbps: Math.round((s.bytesSince * 8) / dt),
    decoded: s.decoded,
    drawn: s.drawn,
    dropQueue: s.dropQueue,
    dropKey: s.dropKey,
    dropError: s.dropError,
    queue: queueDepth(s),
    sinceDrawnMs: s.lastFrameAt > 0 ? Math.round(now - s.lastFrameAt) : -1
  };
  s.statsAt = now;
  s.decoded = 0;
  s.drawn = 0;
  s.dropQueue = 0;
  s.dropKey = 0;
  s.dropError = 0;
  s.latencySum = 0;
  s.latencyCount = 0;
  s.bytesSince = 0;
  safeInvoke(s, "OnVideoStats", stats);
}

function decoderFailed(s, message) {
  if (s.ended) return;
  s.dropError++;
  s.decoderErrors++;
  if (s.decoderErrors > DECODER_ERROR_LIMIT) {
    finish(s, "decoder error: " + message, 0, true);
    return;
  }
  closeDecoder(s);
  s.codec = null;
  s.needKey = true;
  s.backlog = false;
  s.pending.clear();
  if (s.sps) configure(s);
}

function configure(s) {
  let info;
  try {
    info = parseSps(s.sps);
  } catch {
    return;
  }
  if (s.decoder && s.decoder.state === "configured" && info.codec === s.codec && info.width === s.width && info.height === s.height) return;
  s.codec = info.codec;
  s.width = info.width;
  s.height = info.height;
  closeDecoder(s);
  try {
    const d = new VideoDecoder({
      output: (frame) => onFrame(s, frame),
      error: (e) => decoderFailed(s, e && e.message ? e.message : String(e))
    });
    d.configure({ codec: info.codec, optimizeForLatency: true });
    s.decoder = d;
    s.needKey = true;
  } catch (e) {
    finish(s, "decoder error: " + errText(e), 0, false);
  }
}

function submit(s, nal, isKey, now) {
  const d = s.decoder;
  if (!d || d.state !== "configured") {
    s.dropError++;
    return;
  }
  if (isKey) {
    if (!s.sps || !s.pps) {
      s.dropKey++;
      return;
    }
    s.needKey = false;
  } else if (s.needKey) {
    s.dropKey++;
    return;
  } else if (s.backlog ? d.decodeQueueSize > 1 : d.decodeQueueSize > s.opts.maxQueue) {
    s.backlog = true;
    s.dropQueue++;
    return;
  } else {
    s.backlog = false;
  }
  const data = isKey ? annexB([s.sps, s.pps, nal]) : annexB([nal]);
  const timestamp = s.ts;
  s.ts += CHUNK_US;
  s.pending.set(timestamp, now);
  while (s.pending.size > PENDING_CAP) s.pending.delete(s.pending.keys().next().value);
  try {
    d.decode(new EncodedVideoChunk({ type: isKey ? "key" : "delta", timestamp, data }));
  } catch (e) {
    s.pending.delete(timestamp);
    decoderFailed(s, errText(e));
  }
}

function handleNal(s, nal, now) {
  if (s.ended || nal.length === 0) return;
  const type = nal[0] & 0x1f;
  if (type === 7) {
    s.sps = nal;
    configure(s);
  } else if (type === 8) {
    s.pps = nal;
  } else if (type === 5 || type === 1) {
    submit(s, nal, type === 5, now);
  }
}

function sleep(ms, signal) {
  return new Promise((resolve) => {
    const t = setTimeout(resolve, ms);
    if (signal) signal.addEventListener("abort", () => { clearTimeout(t); resolve(); }, { once: true });
  });
}

async function open(s) {
  for (let attempt = 0; ; attempt++) {
    const response = await fetch(s.url, { cache: "no-store", credentials: "same-origin", signal: s.ctrl.signal });
    if (response.ok) return response;
    const body = await response.text();
    if (response.status === 409 && attempt === 0 && !s.stopped) {
      await sleep(1000, s.ctrl.signal);
      if (s.stopped) throw new DOMException("stopped", "AbortError");
      continue;
    }
    const err = new Error(describeHttp(response.status, body));
    err.status = response.status;
    throw err;
  }
}

async function pump(s) {
  let reason = "the server closed the stream";
  let status = 0;
  try {
    const response = await open(s);
    if (!response.body) throw new Error("the response had no body");
    s.reader = response.body.getReader();
    let rest = new Uint8Array(0);
    for (;;) {
      const { done, value } = await s.reader.read();
      if (done || s.stopped) break;
      s.bytesSince += value.byteLength;
      const split = splitNals(concatBytes(rest, value));
      rest = split.rest;
      const now = performance.now();
      for (const nal of split.nals) handleNal(s, nal, now);
    }
  } catch (e) {
    const aborted = s.stopped || e?.name === "AbortError";
    reason = aborted ? "stopped" : errText(e);
    status = typeof e?.status === "number" ? e.status : 0;
  }
  finish(s, s.stopped ? "stopped" : reason, status, false);
}

export function startVideo(canvas, url, dotnet, opts) {
  if (!canvas || !supportsVideo()) return false;
  const o = Object.assign({ maxQueue: 4, key: "", token: "" }, opts || {});
  const key = o.key || url;
  const onCanvas = videoSessions.get(canvas);
  if (onCanvas) finish(onCanvas, "stopped", 0, false);
  const onKey = liveVideos.get(key);
  if (onKey) finish(onKey, "stopped", 0, false);
  sweepOrphans(null);
  const ctx = canvas.getContext("2d", { alpha: false, desynchronized: true });
  if (!ctx) return false;
  const s = {
    id: ++videoSeq,
    key,
    token: o.token,
    canvas,
    ctx,
    scratch: null,
    scratchCtx: null,
    url,
    dotnet,
    opts: o,
    ctrl: new AbortController(),
    reader: null,
    decoder: null,
    decoderErrors: 0,
    sps: null,
    pps: null,
    codec: null,
    width: 0,
    height: 0,
    needKey: true,
    backlog: false,
    ts: 0,
    pending: new Map(),
    frameW: 0,
    frameH: 0,
    decoded: 0,
    drawn: 0,
    dropQueue: 0,
    dropKey: 0,
    dropError: 0,
    latencySum: 0,
    latencyCount: 0,
    bytesSince: 0,
    statsAt: performance.now(),
    startedAt: performance.now(),
    lastFrameAt: 0,
    statsTimer: 0,
    stopped: false,
    ended: false
  };
  videoSessions.set(canvas, s);
  liveVideos.set(key, s);
  s.statsTimer = setInterval(() => tickStats(s), 1000);
  pump(s);
  return true;
}

export function stopVideo(canvas, key) {
  const onCanvas = canvas ? videoSessions.get(canvas) : null;
  if (onCanvas) finish(onCanvas, "stopped", 0, false);
  const onKey = key ? liveVideos.get(key) : null;
  if (onKey) finish(onKey, "stopped", 0, false);
  sweepOrphans(null);
}

export function measureCanvas(canvas) {
  if (!canvas) return { w: 0, h: 0, frameW: 0, frameH: 0 };
  const s = videoSessions.get(canvas);
  return {
    w: canvas.clientWidth,
    h: canvas.clientHeight,
    frameW: s ? s.frameW : canvas.width,
    frameH: s ? s.frameH : canvas.height
  };
}

export function samplePixel(canvas, fx, fy) {
  if (!canvas) return null;
  const s = videoSessions.get(canvas);
  if (!s || s.frameW <= 0) return null;
  return readPixel(s, fx, fy);
}

export function watchPoints(canvas, points, opts) {
  if (!canvas) return false;
  const list = Array.isArray(points) ? points : [];
  if (list.length === 0) {
    watchSessions.delete(canvas);
    return true;
  }
  const o = { tolerance: 48, cooldownMs: 2500, ...(opts || {}) };
  watchSessions.set(canvas, {
    points: list.map((p) => ({ id: p.id, fx: p.fx, fy: p.fy, r: p.r, g: p.g, b: p.b })),
    tolerance: o.tolerance,
    cooldownMs: o.cooldownMs,
    cooldowns: new Map(),
    checkedAt: 0
  });
  return true;
}

export function clearWatchPoints(canvas) {
  if (!canvas) return;
  watchSessions.delete(canvas);
}

const stageSessions = new WeakMap();
const ACCENT = "#ef7559";

function safeStage(s, method, ...args) {
  if (!s.dotnet) return;
  try {
    const p = s.dotnet.invokeMethodAsync(method, ...args);
    if (p && typeof p.catch === "function") p.catch(() => {});
  } catch {
  }
}

function clamp01(v) {
  if (v < 0) return 0;
  if (v > 1) return 1;
  return v;
}

function normPoint(s, ev) {
  const rect = s.media.getBoundingClientRect();
  if (rect.width <= 0 || rect.height <= 0) return null;
  return {
    fx: clamp01((ev.clientX - rect.left) / rect.width),
    fy: clamp01((ev.clientY - rect.top) / rect.height)
  };
}

function localPoint(s, ev) {
  const rect = s.stage.getBoundingClientRect();
  return { x: ev.clientX - rect.left, y: ev.clientY - rect.top };
}

function ripple(s, x, y, kind) {
  const size = kind === "long" ? 48 : 30;
  const dot = document.createElement("span");
  dot.style.cssText = "position:absolute;pointer-events:none;z-index:6;border-radius:9999px;border:2px solid "
    + ACCENT + ";background:rgba(239,117,89,0.28);left:" + (x - size / 2) + "px;top:" + (y - size / 2)
    + "px;width:" + size + "px;height:" + size + "px;";
  s.stage.appendChild(dot);
  const anim = dot.animate(
    [{ transform: "scale(0.3)", opacity: 0.9 }, { transform: "scale(1)", opacity: 0 }],
    { duration: kind === "long" ? 520 : 320, easing: "ease-out" }
  );
  anim.finished.then(() => dot.remove(), () => dot.remove());
}

function dragLine(s, x1, y1, x2, y2) {
  if (!s.line) {
    s.line = document.createElement("span");
    s.line.style.cssText = "position:absolute;height:2px;transform-origin:0 50%;pointer-events:none;z-index:5;opacity:0.75;background:"
      + ACCENT + ";";
    s.stage.appendChild(s.line);
  }
  const dx = x2 - x1;
  const dy = y2 - y1;
  const len = Math.sqrt(dx * dx + dy * dy);
  const ang = Math.atan2(dy, dx) * 180 / Math.PI;
  s.line.style.left = x1 + "px";
  s.line.style.top = y1 + "px";
  s.line.style.width = len + "px";
  s.line.style.transform = "rotate(" + ang + "deg)";
}

function clearLine(s) {
  if (!s.line) return;
  s.line.remove();
  s.line = null;
}

function clearTimers(s) {
  if (s.timer) {
    clearTimeout(s.timer);
    s.timer = 0;
  }
  if (s.raf) {
    cancelAnimationFrame(s.raf);
    s.raf = 0;
  }
}

function onStageDown(s, ev) {
  if (s.active || (ev.pointerType === "mouse" && ev.button !== 0)) return;
  if (Date.now() < s.blockedUntil) {
    ev.preventDefault();
    return;
  }

  s.active = true;
  s.moved = false;
  s.handled = false;
  s.pid = ev.pointerId;
  const local = localPoint(s, ev);
  s.startX = local.x;
  s.startY = local.y;
  s.startNorm = normPoint(s, ev);
  ripple(s, local.x, local.y, "tap");
  try {
    s.stage.setPointerCapture(ev.pointerId);
  } catch {
  }
  s.timer = setTimeout(() => {
    if (!s.active || s.moved || s.handled) return;
    s.handled = true;
    s.holding = true;
    ripple(s, s.startX, s.startY, "long");
    if (s.startNorm) safeStage(s, "OnStageHoldStart", s.startNorm.fx, s.startNorm.fy);
  }, s.o.longPressMs);
  ev.preventDefault();
}

function endHold(s, norm) {
  if (!s.holding) return false;
  s.holding = false;
  const at = norm || s.startNorm;
  if (at) safeStage(s, "OnStageHoldEnd", at.fx, at.fy);
  return true;
}

function onStageMove(s, ev) {
  if (!s.active || ev.pointerId !== s.pid) return;
  s.lastEv = ev;
  const local = localPoint(s, ev);
  if (s.holding) return;
  if (!s.moved && Math.abs(local.x - s.startX) < s.o.threshold && Math.abs(local.y - s.startY) < s.o.threshold) return;
  s.moved = true;
  if (s.timer) {
    clearTimeout(s.timer);
    s.timer = 0;
  }
  if (s.raf) return;
  s.raf = requestAnimationFrame(() => {
    s.raf = 0;
    if (!s.lastEv) return;
    const at = localPoint(s, s.lastEv);
    dragLine(s, s.startX, s.startY, at.x, at.y);
  });
}

function onStageUp(s, ev) {
  if (!s.active || ev.pointerId !== s.pid) return;
  s.active = false;
  clearTimers(s);
  clearLine(s);
  try {
    s.stage.releasePointerCapture(ev.pointerId);
  } catch {
  }
  if (endHold(s, normPoint(s, ev))) return;
  if (s.handled) return;
  const end = normPoint(s, ev);
  if (s.moved && s.startNorm && end) {
    safeStage(s, "OnStageSwipe", s.startNorm.fx, s.startNorm.fy, end.fx, end.fy, s.o.swipeMs);
  } else if (s.startNorm) {
    safeStage(s, "OnStageTap", s.startNorm.fx, s.startNorm.fy);
  }
}

function onStageCancel(s) {
  if (!s.active) return;
  s.active = false;
  clearTimers(s);
  clearLine(s);
  endHold(s, null);
}

function onStageWheel(s, ev) {
  if (Math.abs(ev.deltaY) < 1) return;
  ev.preventDefault();
  const p = normPoint(s, ev);
  if (!p) return;
  const dir = ev.deltaY > 0 ? -1 : 1;
  const span = 0.35;
  const y1 = clamp01(p.fy - dir * span / 2);
  const y2 = clamp01(p.fy + dir * span / 2);
  safeStage(s, "OnStageSwipe", p.fx, y1, p.fx, y2, 150);
}

export function bindStage(stage, media, dotnet, opts) {
  if (!stage || !media) return;
  unbindStage(stage);
  const o = { threshold: 8, longPressMs: 600, swipeMs: 200, ...(opts || {}) };
  const s = {
    stage, media, dotnet, o,
    active: false, moved: false, handled: false, holding: false, blockedUntil: 0, pid: -1,
    startX: 0, startY: 0, startNorm: null, lastEv: null,
    timer: 0, raf: 0, line: null
  };
  s.onDown = (ev) => onStageDown(s, ev);
  s.onMove = (ev) => onStageMove(s, ev);
  s.onUp = (ev) => onStageUp(s, ev);
  s.onCancel = () => onStageCancel(s);
  s.onWheel = (ev) => onStageWheel(s, ev);
  stage.style.touchAction = "none";
  stage.addEventListener("pointerdown", s.onDown);
  stage.addEventListener("pointermove", s.onMove);
  stage.addEventListener("pointerup", s.onUp);
  stage.addEventListener("pointercancel", s.onCancel);
  stage.addEventListener("wheel", s.onWheel, { passive: false });
  stageSessions.set(stage, s);
}

export function setStageBlocked(stage, blocked, ms) {
  const s = stageSessions.get(stage);
  if (!s) return;
  s.blockedUntil = blocked ? Date.now() + (ms > 0 ? ms : 600) : 0;
  s.stage.classList.toggle("dcon-blocked", blocked);
  if (blocked && s.active) onStageCancel(s);
}

const keySessions = new WeakMap();
const liveKeys = new Set();
const KEY_NAMES = {
  Enter: "enter",
  Escape: "back",
  Backspace: "del",
  Tab: "tab",
  ArrowUp: "up",
  ArrowDown: "down",
  ArrowLeft: "left",
  ArrowRight: "right",
  Home: "home",
  PageUp: "page-up",
  PageDown: "page-down",
  Delete: "forward-del"
};

function isEditable(target) {
  if (!target || target.nodeType !== 1) return false;
  if (target.isContentEditable) return true;
  const tag = target.tagName;
  return tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT";
}

function repeatAllowed(k, now) {
  if (now - k.rateAt >= 1000) {
    k.rateAt = now;
    k.rateCount = 0;
  }
  if (k.rateCount >= k.o.maxRepeatsPerSecond) return false;
  k.rateCount++;
  return true;
}

function onLiveKey(k, ev) {
  if (ev.ctrlKey || ev.metaKey || ev.altKey) return;
  if (isEditable(ev.target)) return;
  const name = KEY_NAMES[ev.key];
  const printable = !name && typeof ev.key === "string" && ev.key.length === 1;
  if (!name && !printable) return;
  ev.preventDefault();
  if (ev.repeat && !repeatAllowed(k, performance.now())) return;
  if (printable) {
    invokeQuiet(k.dotnet, "OnLiveText", ev.key);
    return;
  }
  invokeQuiet(k.dotnet, "OnLiveKey", name);
}

function dropKeys(k) {
  document.removeEventListener("keydown", k.onKey, true);
  liveKeys.delete(k);
  if (keySessions.get(k.stage) === k) keySessions.delete(k.stage);
}

function detachKeys(stage) {
  for (const k of [...liveKeys]) {
    if (k.stage === stage || !k.stage.isConnected) dropKeys(k);
  }
}

export function captureKeys(stage, dotnet, on, opts) {
  if (!stage) return false;
  detachKeys(stage);
  if (!on || !dotnet) return false;
  const k = {
    stage,
    dotnet,
    o: { maxRepeatsPerSecond: 30, ...(opts || {}) },
    rateAt: 0,
    rateCount: 0
  };
  k.onKey = (ev) => onLiveKey(k, ev);
  document.addEventListener("keydown", k.onKey, true);
  keySessions.set(stage, k);
  liveKeys.add(k);
  return true;
}

export function unbindStage(stage) {
  if (!stage) return;
  detachKeys(stage);
  const s = stageSessions.get(stage);
  if (!s) return;
  stageSessions.delete(stage);
  stage.removeEventListener("pointerdown", s.onDown);
  stage.removeEventListener("pointermove", s.onMove);
  stage.removeEventListener("pointerup", s.onUp);
  stage.removeEventListener("pointercancel", s.onCancel);
  stage.removeEventListener("wheel", s.onWheel);
  stage.style.touchAction = "";
  clearTimers(s);
  clearLine(s);
}
