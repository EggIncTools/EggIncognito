const timeoutMs = 1800000;

function isJson(contentType) {
  return typeof contentType === "string" && contentType.toLowerCase().includes("json");
}

function errorFromText(text, status) {
  const raw = typeof text === "string" ? text : "";
  try {
    const data = JSON.parse(raw);
    if (data && typeof data.error === "string" && data.error) return data.error;
    if (data && typeof data.message === "string" && data.message) return data.message;
  } catch {
  }
  const trimmed = raw.trim();
  if (trimmed) return trimmed.length > 500 ? trimmed.slice(0, 500) : trimmed;
  return `HTTP ${status}`;
}

export async function readError(response) {
  let text = "";
  try {
    text = await response.text();
  } catch {
    text = "";
  }
  return errorFromText(text, response.status);
}

function parseBody(text, contentType) {
  if (!isJson(contentType)) return text;
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

function postWithXhr(url, formData, onProgress) {
  return new Promise(resolve => {
    const xhr = new XMLHttpRequest();
    xhr.open("POST", url);
    xhr.withCredentials = false;
    xhr.timeout = timeoutMs;
    xhr.upload.onprogress = e => {
      if (e.lengthComputable) onProgress(e.loaded, e.total);
    };
    xhr.onload = () => {
      const ok = xhr.status >= 200 && xhr.status < 300;
      const text = xhr.responseText ?? "";
      const body = ok ? parseBody(text, xhr.getResponseHeader("content-type")) : errorFromText(text, xhr.status);
      resolve({ ok, status: xhr.status, body });
    };
    xhr.onerror = () => resolve({ ok: false, status: 0, body: "upload failed" });
    xhr.ontimeout = () => resolve({ ok: false, status: 0, body: "upload timed out" });
    xhr.onabort = () => resolve({ ok: false, status: 0, body: "upload aborted" });
    xhr.send(formData);
  });
}

async function postWithFetch(url, formData) {
  let res;
  try {
    res = await fetch(url, {
      method: "POST",
      body: formData,
      credentials: "same-origin",
      signal: AbortSignal.timeout(timeoutMs)
    });
  } catch (e) {
    return { ok: false, status: 0, body: "upload failed or timed out: " + String(e) };
  }
  if (!res.ok) return { ok: false, status: res.status, body: await readError(res) };
  let text = "";
  try {
    text = await res.text();
  } catch {
    text = "";
  }
  return { ok: true, status: res.status, body: parseBody(text, res.headers.get("content-type")) };
}

export async function postMultipart(url, formData, { onProgress } = {}) {
  if (typeof onProgress === "function") return postWithXhr(url, formData, onProgress);
  return postWithFetch(url, formData);
}

export function formatBytes(n) {
  const value = Number(n) || 0;
  if (value < 1024) return `${Math.round(value)} B`;
  const kb = value / 1024;
  if (kb < 1024) return `${kb.toFixed(1)} KB`;
  const mb = kb / 1024;
  if (mb < 1024) return `${mb.toFixed(1)} MB`;
  return `${(mb / 1024).toFixed(1)} GB`;
}
