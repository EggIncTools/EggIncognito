if (document.prerendering) {
  document.addEventListener("prerenderingchange", () => Blazor.start(), { once: true });
} else {
  Blazor.start();
}
