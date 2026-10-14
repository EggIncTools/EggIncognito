import { postMultipart } from "./http.js";

export async function upload(inputId, url) {
  const el = document.getElementById(inputId);
  const file = el?.files?.[0];
  if (!file) return { status: 0, body: "no file selected" };

  const form = new FormData();
  form.append("file", file, file.name);

  const result = await postMultipart(url, form);
  if (result.ok && el) el.value = "";
  return { status: result.status, body: result.body };
}
