export async function upload(inputId, url) {
  const el = document.getElementById(inputId);
  const file = el?.files?.[0];
  if (!file) return { status: 0, body: "no file selected" };

  const form = new FormData();
  form.append("file", file, file.name);

  let res;
  try {
    res = await fetch(url, {
      method: "POST",
      body: form,
      credentials: "same-origin",
      signal: AbortSignal.timeout(1800000)
    });
  } catch (e) {
    return { status: 0, body: "upload failed or timed out: " + String(e) };
  }

  let body = "";
  try {
    body = await res.text();
  } catch {
    body = "";
  }

  if (res.ok && el) el.value = "";
  return { status: res.status, body };
}
