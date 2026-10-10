export async function postForm(url, formBody) {
  const resp = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: formBody,
  });
  return (await resp.text()).trim();
}
