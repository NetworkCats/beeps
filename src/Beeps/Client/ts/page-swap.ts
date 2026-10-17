// Shows the page as it now is, in place, without reloading: the same address fetched again (it may lead elsewhere,
// signed out to the sign-in page say), its language, title and content swapped in, and the stylesheets and scripts
// it needs added. Used when the page as a whole changes: signed in or out (here or in another tab), or the
// account's language or rights changed. Both main.ts and live.ts use it; `beeps:page` tells either when it happened.

export const PAGE_SWAPPED = 'beeps:page';

declare global {
  interface Window {
    /** The swap in progress, shared by the two bundles that can start one. */
    beepsSwap?: Promise<void>;
  }
}

/** This page's address without a form handler: a page shown after a post would otherwise post again or fail. */
export function pageUrl(): string {
  const url = new URL(window.location.href);
  url.searchParams.delete('handler');
  url.hash = '';
  return url.toString();
}

/** What the slots under `root` show now, by name (a one-time invite code, a new key): a fresh copy has them empty. */
export function takeSlots(root: HTMLElement): Map<string, Node[]> {
  const kept = new Map<string, Node[]>();
  for (const slot of root.querySelectorAll<HTMLElement>('[data-live-slot]')) {
    if (slot.childNodes.length > 0) kept.set(slot.dataset.liveSlot ?? '', [...slot.childNodes]);
  }
  return kept;
}

export function putSlots(root: HTMLElement, kept: Map<string, Node[]>): void {
  for (const [name, nodes] of kept) root.querySelector<HTMLElement>(`[data-live-slot="${CSS.escape(name)}"]`)?.replaceChildren(...nodes);
}

/** Throws when the page cannot be fetched; the caller tries again. One swap at a time: a second call joins the first. */
export function swapPage(): Promise<void> {
  window.beepsSwap ??= swap().finally(() => {
    window.beepsSwap = undefined;
  });
  return window.beepsSwap;
}

async function swap(): Promise<void> {
  const response = await fetch(pageUrl(), { credentials: 'same-origin', headers: { Accept: 'text/html' } });
  const fresh = new DOMParser().parseFromString(await response.text(), 'text/html');
  if (response.url && response.url !== window.location.href) window.history.replaceState(null, '', response.url);
  document.documentElement.lang = fresh.documentElement.lang;
  document.title = fresh.title;
  addMissingAssets(fresh);
  for (const attribute of [...document.body.attributes]) document.body.removeAttribute(attribute.name);
  for (const attribute of fresh.body.attributes) document.body.setAttribute(attribute.name, attribute.value);
  const kept = takeSlots(document.body);
  document.body.replaceChildren(...[...fresh.body.childNodes].map((node) => document.importNode(node, true)));
  putSlots(document.body, kept);
  document.dispatchEvent(new CustomEvent(PAGE_SWAPPED));
}

/** Stylesheets and scripts the new page uses that this one never loaded; copied scripts would not run. */
function addMissingAssets(fresh: Document): void {
  const loadedStyles = new Set([...document.querySelectorAll<HTMLLinkElement>('link[rel="stylesheet"]')].map((l) => l.href));
  for (const link of fresh.querySelectorAll<HTMLLinkElement>('link[rel="stylesheet"]')) {
    if (!loadedStyles.has(link.href)) document.head.append(document.importNode(link, true));
  }
  const loadedScripts = new Set([...document.querySelectorAll<HTMLScriptElement>('script[src]')].map((s) => s.src));
  for (const script of fresh.querySelectorAll<HTMLScriptElement>('script[src]')) {
    if (loadedScripts.has(script.src)) continue;
    const copy = document.createElement('script');
    copy.type = script.type;
    copy.src = script.src;
    document.head.append(copy);
  }
}
