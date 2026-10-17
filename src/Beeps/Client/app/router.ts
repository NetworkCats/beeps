// Moving between pages without loading them in full, as a single-page app would. Links and forms of this site are
// followed in the background: the page they lead to is fetched (as HTML, so it is exactly what a reload shows), its
// content is put in place of this one's, and its islands are taken over. The script, the live connection and what
// the app knows of each beep stay. Going back shows the page as it was left (more pages loaded, where it was
// scrolled), then lets it catch up.
//
// A page signed in as someone else, or signed out, is loaded in full instead: it needs other scripts.
//
// Signed in, a link the mouse rests on is fetched at once (the page of a beep too, which opens when clicked anywhere on
// it), so following it shows the page without waiting.
import { markSending } from './busy.ts';
import { readConfig, viewer } from './config.ts';
import { confirmAction } from './dialogs.ts';
import { loadStrings } from './i18n.ts';
import { mountIslands, snapshotIslands, unmountIslands } from './islands.tsx';
import type { Envelope } from './props.ts';

/**
 * Dispatched on the document once another page is shown; `detail.restored` when gone back to as it was left,
 * `detail.ahead` when it was fetched ahead (hovered) and so may have been drawn a while ago.
 */
export const PAGE_SHOWN = 'beeps:shown';

export interface PageShown {
  restored: boolean;
  ahead: boolean;
}

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

/**
 * Stylesheets the new page uses that this one never loaded (the admin pages', the avatar cropper's); settles once they
 * have loaded (or failed to), so the page is never shown without them. They were fetched ahead, so this is quick.
 */
function addMissingStyles(fresh: Document): Promise<void> {
  const loaded = new Set([...document.querySelectorAll<HTMLLinkElement>('link[rel="stylesheet"]')].map((l) => l.href));
  const adding: Array<Promise<void>> = [];
  for (const link of fresh.querySelectorAll<HTMLLinkElement>('link[rel="stylesheet"]')) {
    if (loaded.has(link.href)) continue;
    const added = document.importNode(link, true);
    adding.push(new Promise((resolve) => {
      added.addEventListener('load', () => resolve(), { once: true });
      added.addEventListener('error', () => resolve(), { once: true });
    }));
    document.head.append(added);
  }
  return Promise.all(adding).then(() => {});
}

function shown(detail: PageShown): void {
  readConfig();
  document.dispatchEvent(new CustomEvent<PageShown>(PAGE_SHOWN, { detail }));
}

/** A new page is announced as the browser would: focus goes to its content (the old link is gone), not to the top of the document. */
function focusContent(): void {
  const main = document.getElementById('main');
  if (!main) return;
  main.tabIndex = -1;
  main.focus({ preventScroll: true });
}

/**
 * Takes over the islands of the page now in the body; settles once they are drawn (a page's component is loaded the
 * first time a page has it, the text when the page is in another language). One whose code or text cannot be loaded
 * (offline, or the site was updated and the old chunk is gone) has the page loaded in full instead.
 */
function mount(saved?: ReadonlyMap<string, Envelope>): Promise<void> {
  return loadStrings().then(() => mountIslands(document.body, saved)).catch(() => window.location.assign(pageUrl()));
}

function swapIn(fresh: Document, ahead: boolean): Promise<void> {
  unmountIslands();
  document.documentElement.lang = fresh.documentElement.lang;
  document.title = fresh.title;
  setBodyAttributes([...fresh.body.attributes].map((a) => [a.name, a.value]));
  document.body.replaceChildren(...[...fresh.body.childNodes].map((node) => document.importNode(node, true)));
  readConfig();
  return mount().then(() => shown({ restored: false, ahead }));
}

function restore(snapshot: Snapshot): Promise<void> {
  unmountIslands();
  document.title = snapshot.title;
  setBodyAttributes(snapshot.bodyAttributes);
  document.body.innerHTML = snapshot.body;
  readConfig();
  return mount(snapshot.islands).then(() => {
    window.scrollTo(0, snapshot.scroll);
    shown({ restored: true, ahead: false });
  });
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
  /** Fetched ahead, when the link was hovered. */
  ahead?: boolean;
  posted?: boolean;
  hash?: string;
}

/** Shows what came back for a link or a form; anything that is not a page of this site for the same person is loaded in full. */
async function show(response: Response, options: ShowOptions, signal: AbortSignal): Promise<void> {
  if (!response.headers.get('Content-Type')?.includes('text/html')) {
    window.location.assign(response.url);
    return;
  }
  const fresh = new DOMParser().parseFromString(await response.text(), 'text/html');
  if ((fresh.body.dataset.auth ?? '') !== (document.body.dataset.auth ?? '')) {
    window.location.assign(response.url);
    return;
  }
  await addMissingStyles(fresh);
  if (signal.aborted) return;
  const url = response.url + (options.hash ?? '');
  const scroll = window.scrollY;
  manual();
  // An error page (not found, forbidden) may turn into the page again when what it is about comes back.
  if (response.ok) delete document.documentElement.dataset.liveStatus;
  else document.documentElement.dataset.liveStatus = String(response.status);
  let drawn: Promise<void>;
  if (options.replace) {
    drawn = swapIn(fresh, options.ahead ?? false);
    window.history.replaceState({ beeps: current }, '', url);
  } else {
    save();
    current = { key: newKey(), index: current.index + 1, posted: options.posted };
    drawn = swapIn(fresh, options.ahead ?? false);
    window.history.pushState({ beeps: current }, '', url);
  }
  // Scrolled once the islands are drawn: what is scrolled to may be in one.
  await drawn;
  if (signal.aborted) return;
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
    const ahead = init.method === undefined && !options.replace ? await takeFetchedAhead(url) : null;
    if (controller.signal.aborted) return;
    const response = ahead ?? await fetch(url, { ...init, credentials: 'same-origin', signal: controller.signal, headers: { Accept: 'text/html' } });
    if (controller.signal.aborted) return;
    await show(response, { ...options, ahead: ahead !== null }, controller.signal);
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

// ---- Fetched ahead on hover --------------------------------------------------------------------

// How long the mouse rests on a link before its page is fetched (passing over it on the way elsewhere fetches nothing).
const HOVER_DELAY = 80;
// A page fetched ahead is shown at most this long after; the live connection catches it up from when it was drawn.
const AHEAD_TTL = 30_000;
const MAX_AHEAD = 10;

interface FetchedAhead {
  at: number;
  response: Promise<Response | null>;
}

const fetchedAhead = new Map<string, FetchedAhead>();
let hovered: string | undefined;
let hoverTimer: number | undefined;

function withoutHash(url: string): string {
  const target = new URL(url, window.location.href);
  target.hash = '';
  return target.href;
}

/** The page fetched ahead for this address, once: null when there is none (or it failed), so it is fetched now. */
async function takeFetchedAhead(url: string): Promise<Response | null> {
  const key = withoutHash(url);
  const entry = fetchedAhead.get(key);
  fetchedAhead.delete(key);
  if (!entry || Date.now() - entry.at > AHEAD_TTL) return null;
  return entry.response;
}

function fetchAhead(url: string): void {
  const key = withoutHash(url);
  const entry = fetchedAhead.get(key);
  if (entry && Date.now() - entry.at <= AHEAD_TTL) return;
  // A page that did not come back as one (an error, a redirect off the site) is fetched again when followed.
  const response = fetch(key, { credentials: 'same-origin', headers: { Accept: 'text/html' } })
    .then((r) => (r.ok && r.headers.get('Content-Type')?.includes('text/html') ? r : null), () => null);
  fetchedAhead.set(key, { at: Date.now(), response });
  while (fetchedAhead.size > MAX_AHEAD) fetchedAhead.delete(fetchedAhead.keys().next().value as string);
}

/** The page the element under the mouse leads to: a link's, or a beep's (the card opens it, components/BeepCard.tsx). */
function hoverTarget(element: Element): URL | undefined {
  const link = element.closest<HTMLAnchorElement>('a[href]');
  if (link) {
    if ((link.target && link.target !== '_self') || link.hasAttribute('download') || goesBack(link)) return undefined;
    return new URL(link.href);
  }
  const permalink = element.closest('article.beep:not(.beep-focused)')?.querySelector<HTMLAnchorElement>('a.beep-time');
  return permalink ? new URL(permalink.href) : undefined;
}

function onPointerOver(event: PointerEvent): void {
  if (event.pointerType !== 'mouse' || !(event.target instanceof Element)) return;
  const url = hoverTarget(event.target);
  const here = url !== undefined && url.pathname === window.location.pathname && url.search === window.location.search;
  const target = url && isPage(url) && !here ? url.href : undefined;
  if (target === hovered) return;
  window.clearTimeout(hoverTimer);
  hovered = target;
  if (target) hoverTimer = window.setTimeout(() => fetchAhead(target), HOVER_DELAY);
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
  // Whatever it does (the islands' forms send themselves), the pages fetched ahead may no longer be what they would be.
  fetchedAhead.clear();
  if (event.defaultPrevented || !(event.target instanceof HTMLFormElement)) return;
  const form = event.target;
  const submitter = event.submitter instanceof HTMLButtonElement || event.submitter instanceof HTMLInputElement ? event.submitter : null;
  const method = (submitter?.getAttribute('formmethod') ?? form.getAttribute('method') ?? 'get').toLowerCase();
  const url = new URL(submitter?.getAttribute('formaction') ?? form.action, window.location.href);
  if (method === 'dialog' || !isPage(url)) return;
  event.preventDefault();
  const data = new FormData(form, submitter);
  if (form.dataset.confirm && !(await confirmAction(form.dataset.confirm))) return;
  const sent = markSending(form, submitter);
  try {
    if (method === 'get') {
      url.search = new URLSearchParams([...data].map(([name, value]) => [name, String(value)])).toString();
      await visit(url.href).catch(() => {});
    } else {
      await request(url.href, { method: 'POST', body: data }, { posted: true }).catch(() => {
        // The page stays as it was; sent the plain way, the browser shows what went wrong.
        form.submit();
      });
    }
  } finally {
    sent();
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
  if (snapshot && snapshot.url === window.location.href) void restore(snapshot);
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
  // A page signed in as someone else is loaded in full, so this holds for as long as the app runs.
  if (viewer.value.human) document.addEventListener('pointerover', onPointerOver);
  window.addEventListener('popstate', onPop);
}
