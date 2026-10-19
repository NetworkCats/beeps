// Moving between pages without loading them in full, as a single-page app would. Links and forms of this site are
// followed in the background. Signed in, what comes back is the page's data (JSON from /page-data,
// Pages/Islands/PageDataMiddleware.cs): its islands' props, its content column with the islands empty, and what of the
// layout is its own (PageShell.cs). The content column is put in place of this one's and the components draw the
// islands from their props; the rest of the layout and its islands stay. The first page of a tab is shown the same way:
// the server sends an app shell, the layout with a skeleton for the content (AppShellAttribute.cs). Signed out, the
// page comes as HTML (exactly what a reload shows, the server draws the islands too, for search engines) and its body
// is put in place of this one's. The script, the live connection and what the app knows of each beep stay. Going back
// shows the page as it was left (more pages loaded, where it was scrolled), then lets it catch up. The answer to a form
// takes the form's place in the tab's history, as the page already shown does when it is visited again: going back
// never lands on a form already sent, or on the same page.
//
// A page signed in as someone else, signed out, or in another language is loaded in full instead: it needs other
// scripts, or its layout reads differently.
//
// Signed in, a link the mouse rests on is fetched at once, as JSON (the page of a beep too, which opens when clicked
// anywhere on it), so following it shows the page without waiting.
import { signal } from '@preact/signals';
import { markSending } from './busy.ts';
import { keepViewer, readConfig } from './config.ts';
import { confirmAction } from './dialogs.ts';
import { pageDataUrl, pageOf } from './http.ts';
import { loadStrings } from './i18n.ts';
import { loadComponent, mountIslands, snapshotIslands, unmountIslands } from './islands.tsx';
import type { Envelope, IslandsPage, PageShell, PageView } from './props.ts';
import { showToast } from './toasts.ts';

/**
 * Dispatched on the document once another page is shown; `detail.restored` when gone back to as it was left,
 * `detail.ahead` when it was fetched ahead (hovered) and so may have been drawn a while ago.
 */
export const PAGE_SHOWN = 'beeps:shown';

export interface PageShown {
  restored: boolean;
  ahead: boolean;
}

/**
 * The page's address (path and query) once one has been shown in place, for what the layout's islands send along (the
 * language switch comes back to it); until then the one the server drew them with.
 */
export const pagePath = signal<string | undefined>(undefined);

interface Entry {
  key: string;
  /** How many pages of this site came before this one in the tab, as far as this script knows. */
  index: number;
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
// The address of the page shown, which its snapshot is kept under: going back or forward, the address bar already
// shows the page gone to by the time this one is saved.
let shownUrl = '';
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

/** Signed in (data-auth): the components draw all there is to see, from the pages' data. */
function signedIn(): boolean {
  return Boolean(document.body.dataset.auth);
}

/** Signed in, a page is fetched as its data, for the components to draw; signed out as HTML, which the server draws. */
function fetchPage(url: string, init: RequestInit = {}): Promise<Response> {
  return signedIn()
    ? fetch(pageDataUrl(url), { ...init, credentials: 'same-origin', headers: { Accept: 'application/json' } })
    : fetch(url, { ...init, credentials: 'same-origin', headers: { Accept: 'text/html' } });
}

/** Whether two addresses are the same page: path and query, whatever their fragments. */
function samePage(a: string, b: string): boolean {
  const first = new URL(a);
  const second = new URL(b);
  return first.pathname === second.pathname && first.search === second.search;
}

function isJson(response: Response): boolean {
  return response.headers.get('Content-Type')?.includes('application/json') ?? false;
}

function isHtml(response: Response): boolean {
  return response.headers.get('Content-Type')?.includes('text/html') ?? false;
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
    url: shownUrl,
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
function addMissingStyles(hrefs: Iterable<string>): Promise<void> {
  const loaded = new Set([...document.querySelectorAll<HTMLLinkElement>('link[rel="stylesheet"]')].map((l) => l.href));
  const adding: Array<Promise<void>> = [];
  for (const href of hrefs) {
    const url = new URL(href, window.location.href).href;
    if (loaded.has(url)) continue;
    loaded.add(url);
    const added = document.createElement('link');
    added.rel = 'stylesheet';
    added.href = url;
    adding.push(new Promise((resolve) => {
      added.addEventListener('load', () => resolve(), { once: true });
      added.addEventListener('error', () => resolve(), { once: true });
    }));
    document.head.append(added);
  }
  return Promise.all(adding).then(() => {});
}

/**
 * The stylesheets a page sent as JSON names (PageShell.cs), by the addresses the layout links them under or fetched
 * them ahead under (data-style): fingerprinted, which only the server knows how to do.
 */
function stylesNamed(names: string[]): string[] {
  return names.flatMap((name) => {
    const link = document.querySelector<HTMLLinkElement>(`link[data-style="${name}"]`);
    return link ? [link.href] : [];
  });
}

function stylesOf(fresh: Document): string[] {
  return [...fresh.querySelectorAll<HTMLLinkElement>('link[rel="stylesheet"]')].map((link) => link.getAttribute('href') ?? '');
}

function shown(detail: PageShown): void {
  readConfig();
  // Called once the page is drawn, so after its address is in the history.
  const url = new URL(pageUrl());
  pagePath.value = url.pathname + url.search;
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
 * Takes over the islands under `root` (the body, or the content column); settles once they are drawn (a page's
 * component is loaded the first time a page has it, the text when the page is in another language). One whose code or
 * text cannot be loaded (offline, or the site was updated and the old chunk is gone) has the page loaded in full instead.
 */
function mount(root: ParentNode, saved?: ReadonlyMap<string, Envelope>): Promise<void> {
  return loadStrings().then(() => mountIslands(root, saved)).catch(() => window.location.assign(pageUrl()));
}

/** The layout as the page has it (PageShell.cs): its item in the navigation, the parts it leaves out, how wide. */
function showShell(shell: PageShell): void {
  for (const item of document.querySelectorAll<HTMLElement>('[data-nav]')) {
    if (item.dataset.nav === shell.nav) item.setAttribute('aria-current', 'page');
    else item.removeAttribute('aria-current');
  }
  for (const part of document.querySelectorAll<HTMLElement>('[data-part]')) part.hidden = shell.hidden.includes(part.dataset.part ?? '');
  document.querySelector('.shell')?.classList.toggle('shell-wide', shell.wide ?? false);
}

/** The app's config as the page's data says it, where the layout of a page the server drew keeps it (config.ts). */
function setAppConfig(view: PageView): void {
  let script = document.getElementById('app-config');
  if (!script) {
    script = document.createElement('script');
    script.id = 'app-config';
    script.setAttribute('type', 'application/json');
    document.body.append(script);
  }
  script.textContent = JSON.stringify(view.config);
  readConfig();
}

/** A page's data: the content column is put in place, its islands drawn from their props; the layout's stay as they are. */
function swapView(main: HTMLElement, answer: IslandsPage, view: PageView, ahead: boolean): Promise<void> {
  unmountIslands(main);
  document.title = answer.title;
  document.body.dataset.liveFrom = String(view.liveFrom);
  // Who is signed in now, the token, where the text is: before the islands are drawn.
  setAppConfig(view);
  showShell(view.shell);
  main.innerHTML = view.main;
  if (answer.notice) showToast(answer.notice);
  if (answer.alert) showToast(answer.alert, true);
  return mount(main, new Map(Object.entries(answer.islands))).then(() => shown({ restored: false, ahead }));
}

function swapIn(fresh: Document, ahead: boolean): Promise<void> {
  unmountIslands();
  document.documentElement.lang = fresh.documentElement.lang;
  document.title = fresh.title;
  setBodyAttributes([...fresh.body.attributes].map((a) => [a.name, a.value]));
  document.body.replaceChildren(...[...fresh.body.childNodes].map((node) => document.importNode(node, true)));
  readConfig();
  return mount(document.body).then(() => shown({ restored: false, ahead }));
}

function restore(snapshot: Snapshot): Promise<void> {
  unmountIslands();
  document.title = snapshot.title;
  setBodyAttributes(snapshot.bodyAttributes);
  document.body.innerHTML = snapshot.body;
  keepViewer();
  readConfig();
  return mount(document.body, snapshot.islands).then(() => {
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
  /** Instead of a new history entry: going back, the same page shown anew, or the answer to a form. */
  replace?: boolean;
  /** Where it was scrolled, for the same page shown anew. */
  keepScroll?: boolean;
  /** Fetched ahead, when the link was hovered. */
  ahead?: boolean;
  /** The first page of the tab, in place of an app shell's skeleton: nothing to move the focus from. */
  first?: boolean;
  hash?: string;
  /** Failing to fetch it throws for the caller to try again, rather than loading the page in full. */
  retry?: boolean;
}

/** Puts what came back in place of this page, given whether it was fetched ahead. */
type Swap = (ahead: boolean) => Promise<void>;

/**
 * What came back for a link or a form, made ready to be shown: the stylesheets it needs loaded, and the components of
 * its islands. Null when it is to be loaded in full instead: not a page of this site, or one for someone else.
 */
async function prepare(response: Response): Promise<Swap | null> {
  const auth = document.body.dataset.auth ?? '';
  if (isJson(response)) {
    const answer = (await response.json()) as IslandsPage;
    const view = answer.page;
    const main = document.getElementById('main');
    if (!main || (view.auth ?? '') !== auth || view.lang !== document.documentElement.lang) return null;
    // One that cannot be loaded has the page loaded in full once it is shown (mount).
    const components = Object.keys(answer.islands).map((name) => loadComponent(name).catch(() => undefined));
    await Promise.all([addMissingStyles(stylesNamed(view.shell.styles)), ...components]);
    return (ahead) => swapView(main, answer, view, ahead);
  }
  if (!isHtml(response)) return null;
  const fresh = new DOMParser().parseFromString(await response.text(), 'text/html');
  if ((fresh.body.dataset.auth ?? '') !== auth) return null;
  await addMissingStyles(stylesOf(fresh));
  return (ahead) => swapIn(fresh, ahead);
}

/** Shows what came back for a link or a form; anything that is not a page of this site for the same person is loaded in full. */
async function show(response: Response, options: ShowOptions, signal: AbortSignal): Promise<void> {
  const swap = await prepare(response);
  if (signal.aborted) return;
  const address = pageOf(response.url);
  if (!swap) {
    window.location.assign(address);
    return;
  }
  const url = address + (options.hash ?? '');
  const scroll = window.scrollY;
  manual();
  // An error page (not found, forbidden) may turn into the page again when what it is about comes back.
  if (response.ok) delete document.documentElement.dataset.liveStatus;
  else document.documentElement.dataset.liveStatus = String(response.status);
  let drawn: Promise<void>;
  if (options.replace) {
    drawn = swap(options.ahead ?? false);
    window.history.replaceState({ beeps: current }, '', url);
  } else {
    save();
    current = { key: newKey(), index: current.index + 1 };
    drawn = swap(options.ahead ?? false);
    window.history.pushState({ beeps: current }, '', url);
  }
  shownUrl = window.location.href;
  // Scrolled once the islands are drawn: what is scrolled to may be in one.
  await drawn;
  if (signal.aborted) return;
  if (options.keepScroll) {
    window.scrollTo(0, scroll);
  } else {
    if (!options.first) focusContent();
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
    const response = ahead ?? await fetchPage(url, { ...init, signal: controller.signal });
    if (controller.signal.aborted) return;
    await show(response, { ...options, ahead: ahead !== null }, controller.signal);
  } catch (error) {
    if (controller.signal.aborted) return;
    // Offline, or the request failed: the browser shows what is wrong.
    if (init.method !== 'POST' && !options.retry) window.location.assign(url);
    throw error;
  } finally {
    if (visiting === controller) {
      visiting = undefined;
      delete document.documentElement.dataset.loading;
    }
  }
}

/**
 * The page of an app shell (signed in, AppShellAttribute.cs): its data comes, and the page is shown in place of the
 * skeleton; then the layout's islands are drawn too, now the app's config has come with it.
 */
export function showFirstPage(): Promise<void> {
  return request(pageUrl(), {}, { replace: true, first: true, hash: window.location.hash }).then(() => mount(document.body));
}

/** Goes to another page of the site; `replace`, in place of this one (it showed a form, and this is its answer). */
export function visit(url: string, replace = false): Promise<void> {
  const target = new URL(url, window.location.href);
  const here = target.pathname === window.location.pathname && target.search === window.location.search;
  return request(target.href, {}, { hash: target.hash, replace: replace || here });
}

/**
 * Shows this page anew as a whole, where it was scrolled: what it is about may be gone (not found now), the person
 * may have been signed out, or their language changed. Throws when it cannot be fetched; the caller tries again.
 */
export function reload(): Promise<void> {
  return request(pageUrl(), {}, { replace: true, keepScroll: true, retry: true });
}

/** Sends a form the plain way and shows the page that comes back: what the browser itself would do. */
export function submitPage(action: string, data: FormData): Promise<void> {
  return request(new URL(action, window.location.href).href, { method: 'POST', body: data }, { replace: true }).catch(() => {});
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
  const response = fetchPage(key)
    .then((r) => (r.ok && (isJson(r) || isHtml(r)) ? r : null), () => null);
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
    && !['/api/', '/stream', '/live/', '/page-data'].some((prefix) => url.pathname.startsWith(prefix));
}

/** A column header's back arrow goes back as the browser would, to where that page was left, when there is one to go to. */
function goesBack(link: HTMLAnchorElement): boolean {
  return link.matches('a.column-back') && current.index > 0;
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
  // Followed now: what the mouse rested on need not be fetched ahead as well.
  window.clearTimeout(hoverTimer);
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
      await request(url.href, { method: 'POST', body: data }, { replace: true }).catch(() => {
        // The page stays as it was; sent the plain way, the browser shows what went wrong.
        form.submit();
      });
    }
  } finally {
    sent();
  }
}

function onPop(event: PopStateEvent): void {
  let entry = entryOf(event.state);
  if (!entry) {
    // A link within the page, or going back or forward to one: the browser scrolls to it.
    if (samePage(window.location.href, shownUrl)) return;
    // Gone back to where a link within another page led (that page, its link, another page, then back): that page is
    // shown again, from the entry that followed it.
    entry = { key: newKey(), index: Math.max(0, current.index - 1) };
    window.history.replaceState({ beeps: entry }, '');
  }
  // A page still on its way was left behind: it must not be shown over the one gone back to.
  visiting?.abort();
  visiting = undefined;
  delete document.documentElement.dataset.loading;
  manual();
  save();
  shownUrl = window.location.href;
  current = entry;
  const snapshot = snapshots.get(entry.key);
  if (snapshot && snapshot.url === window.location.href) void restore(snapshot);
  else void request(window.location.href, {}, { replace: true, hash: window.location.hash }).catch(() => {});
}

export function startRouter(): void {
  // A reload, or coming back from another site, is scrolled by the browser; within the app (manual()) this script does.
  window.addEventListener('pagehide', () => {
    window.history.scrollRestoration = 'auto';
  });
  current = entryOf(window.history.state) ?? { key: newKey(), index: 0 };
  shownUrl = window.location.href;
  window.history.replaceState({ ...(window.history.state as object | null), beeps: current }, '');
  document.addEventListener('click', onClick);
  document.addEventListener('submit', (event) => void onSubmit(event));
  // A page signed in as someone else is loaded in full, so this holds for as long as the app runs.
  if (signedIn()) document.addEventListener('pointerover', onPointerOver);
  window.addEventListener('popstate', onPop);
}
