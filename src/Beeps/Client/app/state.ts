// What the whole app shows wherever it is: the unread count in the navigation and the colour scheme.
import { effect, signal } from '@preact/signals';

/** Unread notifications, as the live connection last said; nothing until it has. */
export const unread = signal(0);

const SCHEMES = new Set(['light', 'dark']);

function pageTheme(): string {
  return document.documentElement.dataset.theme ?? 'system';
}

/** `light`, `dark`, or `system` for the device's: switched here, in another tab, or on another device. */
export const theme = signal('system');

/** Shows the page in its scheme: <html data-theme>, which collection-ui turns into color-scheme, and the browser's bar. */
export function startTheme(): void {
  theme.value = pageTheme();
  effect(() => {
    const value = theme.value;
    const root = document.documentElement;
    if (SCHEMES.has(value)) root.dataset.theme = value;
    else delete root.dataset.theme;
    // Each colour applies to its own scheme, or always when picked by hand.
    for (const meta of document.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"][data-scheme]')) {
      const scheme = meta.dataset.scheme ?? '';
      meta.media = SCHEMES.has(value) ? (value === scheme ? 'all' : 'not all') : `(prefers-color-scheme: ${scheme})`;
    }
  });
}
