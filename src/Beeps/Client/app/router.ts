// Moving between pages without loading them in full, as a single-page app would. Links and forms of this site are
// followed in the background: the page they lead to is fetched (as HTML, so it is exactly what a reload shows), its
// content is put in place of this one's, and its islands are taken over. The script, the live connection and what
// the app knows of each beep stay. Going back shows the page as it was left (more pages loaded, where it was
// scrolled), then lets it catch up.
//
// A page signed in as someone else, or signed out, is loaded in full instead: it needs other scripts.
import { readConfig } from './config.ts';
import { confirmAction } from './dialogs.ts';
import { mountIslands, snapshotIslands, unmountIslands } from './islands.tsx';
import type { Envelope } from './props.ts';

/** Dispatched on the document once another page is shown; `detail.restored` when gone back to as it was left. */
export const PAGE_SHOWN = 'beeps:shown';

interface Entry {
  key: string;
  /** How many pages of this site came before this one in the tab, as far as this script knows. */
  index: number;
  /** This page is the answer to a form: going back would only show the form again (or send it again). */
  posted?: boolean;
}

interface Snapshot {
  url: string;
  title: string;
  body: string;
  bodyAttributes: Array<[string, string]>;
  islands: Map<string, Envelope>;
  scroll: number;
}

const MAX_SNAPSHOTS = 20;
const snapshots = new Map<string, Snapshot>();
let current: Entry = { key: '', index: 0 };
let visiting: AbortController | undefined;

/** From the first page shown in place on, going back is scrolled here, to where the snapshot was taken. */
function manual(): void {
  window.history.scrollRestoration = 'manual';
}

function newKey(): string {
  return Math.random().toString(36).slice(2) + Date.now().toString(36);
}

function entryOf(state: unknown): Entry | undefined {
  if (typeof state !== 'object' || state === null || !('beeps' in state)) return undefined;
  return (state as { beeps: Entry }).beeps;
}

/** This page's address without a form handler: a page shown after a post would otherwise post again or fail. */
export function pageUrl(): string {
  const url = new URL(window.location.href);
  url.searchParams.delete('handler');
  url.hash = '';
  return url.toString();
}

// ---- Showing a page ----------------------------------------------------------------------------

function save(): void {
  snapshots.delete(current.key);
  snapshots.set(current.key, {
    url: window.location.href,
    title: document.title,
    body: document.body.innerHTML,
    bodyAttributes: [...document.body.attributes].map((a) => [a.name, a.value]),
    islands: snapshotIslands(),
    scroll: window.scrollY,
  });
  while (snapshots.size > MAX_SNAPSHOTS) snapshots.delete(snapshots.keys().next().value as string);
}

function setBodyAttributes(attributes: Iterable<[string, string]>): void {
  for (const attribute of [...document.body.attributes]) document.body.removeAttribute(attribute.name);
  for (const [name, value] of attributes) document.body.setAttribute(name, value);
}

/** Stylesheets the new page uses that this one never loaded (the avatar cropper's). */
function addMissingStyles(fresh: Document): void {
  const loaded = new Set([...document.querySelectorAll<HTMLLinkElement>('link[rel="stylesheet"]')].map((l) => l.href));
  for (const link of fresh.querySelectorAll<HTMLLinkElement>('link[rel="stylesheet"]')) {
    if (!loaded.has(link.href)) document.head.append(document.importNode(link, true));
  }
}

function shown(restored: boolean): void {
  readConfig();
  document.dispatchEvent(new CustomEvent(PAGE_SHOWN, { detail: { restored } }));
}

/** A new page is announced as the browser would: focus goes to its content (the old link is gone), not to the top of the document. */
function focusContent(): void {
  const main = document.getElementById('main');
  if (!main) return;
  main.tabIndex = -1;
  main.focus({ preventScroll: true });
}

function swapIn(fresh: Document): void {
  unmountIslands();
  document.documentElement.lang = fresh.documentElement.lang;
  document.title = fresh.title;
  addMissingStyles(fresh);
  setBodyAttributes([...fresh.body.attributes].map((a) => [a.name, a.value]));
  document.body.replaceChildren(...[...fresh.body.childNodes].map((node) => document.importNode(node, true)));
  readConfig();
  mountIslands(document.body);
  shown(false);
}

function restore(snapshot: Snapshot): void {
  unmountIslands();
  document.title = snapshot.title;
  setBodyAttributes(snapshot.bodyAttributes);
  document.body.innerHTML = snapshot.body;
  readConfig();
  mountIslands(document.body, snapshot.islands);
  window.scrollTo(0, snapshot.scroll);
  shown(true);
}

function scrollToTarget(hash: string): void {
  const target = hash ? document.getElementById(decodeURIComponent(hash.slice(1))) : null;
  if (target) target.scrollIntoView();
  else window.scrollTo(0, 0);
}

interface ShowOptions {
  /** Instead of a new history entry: going back, or the same page shown anew. */
  replace?: boolean;
  /** Where it was scrolled, for the same page shown anew. */
  keepScroll?: boolean;
  posted?: boolean;
  hash?: string;
}

/** Shows what came back for a link or a form; anything that is not a page of this site for the same person is loaded in full. */
async function show(response: Response, options: ShowOptions): Promise<void> {
  if (!response.headers.get('Content-Type')?.includes('text/html')) {
    window.location.assign(response.url);
    return;
  }
  const fresh = new DOMParser().parseFromString(await response.text(), 'text/html');
  if ((fresh.body.dataset.auth ?? '') !== (document.body.dataset.auth ?? '')) {
    window.location.assign(response.url);
    return;
  }
  const url = response.url + (options.hash ?? '');
  const scroll = window.scrollY;
  manual();
  // An error page (not found, forbidden) may turn into the page again when what it is about comes back.
  if (response.ok) delete document.documentElement.dataset.liveStatus;
  else document.documentElement.dataset.liveStatus = String(response.status);
  if (options.replace) {
    swapIn(fresh);
    window.history.replaceState({ beeps: current }, '', url);
  } else {
    save();
    current = { key: newKey(), index: current.index + 1, posted: options.posted };
    swapIn(fresh);
    window.history.pushState({ beeps: current }, '', url);
  }
  if (options.keepScroll) {
    window.scrollTo(0, scroll);
  } else {
    focusContent();
    scrollToTarget(options.hash ?? '');
  }
}

async function request(url: string, init: RequestInit, options: ShowOptions): Promise<void> {
  visiting?.abort();
  const controller = new AbortController();
  visiting = controller;
  document.documentElement.dataset.loading = '';
  try {
    const response = await fetch(url, { ...init, credentials: 'same-origin', signal: controller.signal, headers: { Accept: 'text/html' } });
    if (controller.signal.aborted) return;
    await show(response, options);
  } catch (error) {
    if (controller.signal.aborted) return;
    // Offline, or the request failed: the browser shows what is wrong.
    if (init.method !== 'POST') window.location.assign(url);
    throw error;
  } finally {
    if (visiting === controller) {
      visiting = undefined;
      delete document.documentElement.dataset.loading;
    }
  }
}

/** Goes to another page of the site. */
export function visit(url: string): Promise<void> {
  const target = new URL(url, window.location.href);
  return request(target.href, {}, { hash: target.hash });
}

/**
 * Shows this page anew as a whole, where it was scrolled: what it is about may be gone (not found now), the person
 * may have been signed out, or their language changed. Throws when it cannot be fetched; the caller tries again.
 */
export function reload(): Promise<void> {
  return request(pageUrl(), {}, { replace: true, keepScroll: true });
}

/** Sends a form the plain way and shows the page that comes back: what the browser itself would do. */
export function submitPage(action: string, data: FormData): Promise<void> {
  return request(new URL(action, window.location.href).href, { method: 'POST', body: data }, { posted: true }).catch(() => {});
}

// ---- Links, forms and going back ---------------------------------------------------------------

/** Addresses that are pages of this site, not files, the API or the live connection. */
function isPage(url: URL): boolean {
  return url.origin === window.location.origin
    && !/\.[a-z0-9]+$/i.test(url.pathname)
    && !['/api/', '/stream', '/live/'].some((prefix) => url.pathname.startsWith(prefix));
}

/** A column header's back arrow goes back as the browser would, to where that page was left, when there is one to go to. */
function goesBack(link: HTMLAnchorElement): boolean {
  return link.matches('a.column-back') && current.index > 0 && !current.posted;
}

function onClick(event: MouseEvent): void {
  if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
  const link = event.target instanceof Element ? event.target.closest<HTMLAnchorElement>('a[href]') : null;
  if (!link || (link.target && link.target !== '_self') || link.hasAttribute('download')) return;
  const url = new URL(link.href);
  if (!isPage(url)) return;
  // A link within this page (to the reply box, say) is the browser's to follow.
  if (url.pathname === window.location.pathname && url.search === window.location.search && url.hash) return;
  event.preventDefault();
  if (goesBack(link)) window.history.back();
  else void visit(url.href).catch(() => {});
}

/** Forms outside the islands (settings, signing out, searching): sent in the background, their answer shown in place. */
async function onSubmit(event: SubmitEvent): Promise<void> {
  if (event.defaultPrevented || !(event.target instanceof HTMLFormElement)) return;
  const form = event.target;
  const submitter = event.submitter instanceof HTMLButtonElement || event.submitter instanceof HTMLInputElement ? event.submitter : null;
  const method = (submitter?.getAttribute('formmethod') ?? form.getAttribute('method') ?? 'get').toLowerCase();
  const url = new URL(submitter?.getAttribute('formaction') ?? form.action, window.location.href);
  if (method === 'dialog' || !isPage(url)) return;
  event.preventDefault();
  const data = new FormData(form, submitter);
  if (form.dataset.confirm && !(await confirmAction(form.dataset.confirm))) return;
  if (method === 'get') {
    url.search = new URLSearchParams([...data].map(([name, value]) => [name, String(value)])).toString();
    await visit(url.href).catch(() => {});
  } else {
    await request(url.href, { method: 'POST', body: data }, { posted: true }).catch(() => {
      // The page stays as it was; sent the plain way, the browser shows what went wrong.
      form.submit();
    });
  }
}

function onPop(event: PopStateEvent): void {
  const entry = entryOf(event.state);
  // Only the fragment changed (a link within the page): nothing to show.
  if (!entry) return;
  manual();
  save();
  current = entry;
  const snapshot = snapshots.get(entry.key);
  if (snapshot && snapshot.url === window.location.href) restore(snapshot);
  else void request(window.location.href, {}, { replace: true }).catch(() => {});
}

export function startRouter(): void {
  // A reload, or coming back from another site, is scrolled by the browser; within the app (manual()) this script does.
  window.addEventListener('pagehide', () => {
    window.history.scrollRestoration = 'auto';
  });
  current = entryOf(window.history.state) ?? { key: newKey(), index: 0 };
  window.history.replaceState({ ...(window.history.state as object | null), beeps: current }, '');
  document.addEventListener('click', onClick);
  document.addEventListener('submit', (event) => void onSubmit(event));
  window.addEventListener('popstate', onPop);
}
