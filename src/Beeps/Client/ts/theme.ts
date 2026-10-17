// The footer's and the top bar menu's settings (_SiteOptions): <form data-autosubmit> with a <select> takes effect as
// soon as a choice is made; its button is for pages without script.
// The colour scheme (<form data-theme-switch>) switches the page in place: <html data-theme>, which collection-ui
// turns into color-scheme. The choice is sent to /theme in the background (the cookie, and the account when signed
// in, whose other tabs then hear of it through live.ts), so the next page is drawn the same way.

const SCHEMES = new Set(['light', 'dark']);

/** Shows the page in a scheme: `light`, `dark`, or `system` for the device's. */
export function showTheme(theme: string): void {
  const root = document.documentElement;
  if (SCHEMES.has(theme)) root.dataset.theme = theme;
  else delete root.dataset.theme;
  for (const select of document.querySelectorAll<HTMLSelectElement>('[data-theme-switch] select[name="theme"]')) {
    select.value = theme;
  }
  // The browser's bar follows the page: each colour applies to its own scheme, or always when picked by hand.
  for (const meta of document.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"][data-scheme]')) {
    const scheme = meta.dataset.scheme ?? '';
    meta.media = SCHEMES.has(theme) ? (theme === scheme ? 'all' : 'not all') : `(prefers-color-scheme: ${scheme})`;
  }
}

export function initTheme(): void {
  document.addEventListener('change', (event) => {
    const select = event.target;
    if (select instanceof HTMLSelectElement) select.closest<HTMLFormElement>('form[data-autosubmit]')?.requestSubmit();
  });
  document.addEventListener('submit', (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || !form.matches('[data-theme-switch]')) return;
    event.preventDefault();
    const data = new FormData(form);
    showTheme(String(data.get('theme') ?? 'system'));
    // The answer is a redirect back here; only what it stores matters. Offline, the choice lasts until the next page.
    const sent = form.method === 'post'
      ? fetch(form.action, { method: 'POST', body: data, credentials: 'same-origin', redirect: 'manual' })
      : fetch(`${form.action}?${new URLSearchParams([...data].map(([key, value]) => [key, String(value)]))}`, { credentials: 'same-origin', redirect: 'manual' });
    void sent.catch(() => {});
  });
}
