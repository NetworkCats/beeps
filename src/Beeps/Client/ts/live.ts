// Live updates for signed-in members (PLAN 6.10); the layout loads this file for nobody else. One
// EventSource per tab, on <body data-live-stream>, subscribed to what this page shows:
//   <… data-live-watch="home">     the page's topic (empty: nothing gets added, but what is shown still updates)
//   <article data-beep="…">        beeps whose counts, like button and looks update live, and that disappear when
//          data-author="…"         deleted, hidden, or when their author is banned or deleted; drawn again when
//          data-mentions="… …"     the author or someone they mention changes name or avatar
//   [data-inbox-count]             the unread badge
//   [data-live-region="…"          parts of the page drawn again from a fresh copy of it when one of these
//    data-live-actors="… …"]       actors changes (profile, avatar, agents), or when the viewer follows or blocks;
//                                  never while the person is filling in a form in it
//   watch="search" / "admin"       the search results / the admin figures and lists, drawn again as things change
//   [data-live-slot="…"]           what only one response shows (a new invite code): kept when the page is drawn again
//   [data-live-subject="beep:…"    what the page is about: when it is deleted, hidden or its author removed, the page
//    | "actor:…"]                  is shown anew (a reload would not find it either)
//   [data-live-url]                where the page's regions are drawn again from, when its own address can change
//   <li data-from="…">             inbox entries, drawn again when their sender changes
// A form someone is filling in stays as it is when the region around it is drawn again.
// New content is never built here: the page fetches it from /live/…, rendered by the server for this
// viewer, so visibility and markup are exactly those of a reload.

import { showLiked } from './like.ts';
import { compareIds, liveStreamUrl, mergeIds, nextRetryDelay, NO_ID, previousId } from './live-url.ts';
import { PAGE_SWAPPED, pageUrl, putSlots, swapPage, takeSlots } from './page-swap.ts';

const LIVE_BEEPS = '/live/beeps';
const LIVE_NOTIFICATIONS = '/live/notifications';
const AUTH_STATE = '/stream/auth';
// Several events in a row lead to one fetch.
const FETCH_DELAY = 300;
// Search results are asked again at most this often (searching has its own rate limit).
const SEARCH_INTERVAL = 10_000;
// Admin pages: drawn again at most this often. Figures that change with no event of their own (backlogs, dead
// jobs) are watched by the server while an admin page is open (AdminFiguresWorker), which then sends `admin`.
const ADMIN_INTERVAL = 3_000;
// A page about a beep checks it is still there at most this often after a resync (each check renders the whole page).
const SUBJECT_INTERVAL = 5_000;
// Matches LiveFeedService.Limit: how many beeps the server sends at most for one fetch.
const FETCH_LIMIT = 50;
// The inbox page's first page (PageRequest.DefaultLimit) and the most the server lists at once (MaxLimit).
const INBOX_PAGE = 20;
const INBOX_MAX = 100;

interface CountsEntry {
  id: string;
  like_count: number;
  reply_count: number;
}

let source: EventSource | undefined;
let lastEventId: string | undefined;
let retryDelay = 0;
let retryTimer: number | undefined;
// Ended by the server for good (too many tabs open): do not come back.
let closed = false;
// Pushed out by the account's newer tabs: come back when this one is looked at again (pushing out the oldest).
let pushedOut = false;
// This tab is going to another page already (it posted the sign-out, say): that page is the new one to show.
let leaving = false;
// Beeps the server knows this page shows: those it was told on connecting, and those it announced since.
let known = new Set<string>();
// What the next redraws cover; filled by events, emptied by the fetch.
const changedBeeps = new Set<string>();
const changedActors = new Set<string>();
const changedRegions = new Set<string>();
let allRegions = false;
// The inbox catches up for a reason other than new notifications, whose beeps the server then does not know of.
let inboxUnannounced = false;

function watch(): HTMLElement | null {
  return document.querySelector<HTMLElement>('[data-live-watch]');
}

function topic(): string {
  return watch()?.dataset.liveWatch ?? '';
}

function pageBeepIds(): string[] {
  return [...document.querySelectorAll<HTMLElement>('[data-beep]')].map((e) => e.dataset.beep ?? '').filter((id) => id.length > 0);
}

function cards(id: string): HTMLElement[] {
  return [...document.querySelectorAll<HTMLElement>(`[data-beep="${CSS.escape(id)}"]`)];
}

function connect(): void {
  const base = document.body.dataset.liveStream;
  if (!base || closed) return;
  const shown = pageBeepIds();
  // Beyond what one connection may list, the server sends what concerns any beep: all of them are known.
  known = new Set(shown);
  const events = new EventSource(liveStreamUrl(base, topic(), shown, lastEventId, document.body.dataset.liveFrom));
  source = events;
  events.addEventListener('open', () => {
    retryDelay = 0;
  });
  // The browser would reconnect with the same URL; reconnecting here sends the beeps added since.
  events.addEventListener('error', () => {
    events.close();
    if (source !== events || closed) return;
    source = undefined;
    retryDelay = nextRetryDelay(retryDelay);
    retryTimer = window.setTimeout(connect, retryDelay);
    // The session may have ended while the stream was down (no event reached this tab then).
    void checkSignedIn();
  });
  on(events, 'inbox', (data: { unread_count: number }) => {
    showUnread(data.unread_count);
    inboxChanged();
  });
  on(events, 'beep', (data: { id: string }) => {
    known.add(data.id);
    if (cards(data.id).length === 0) schedule(fetchBeeps);
  });
  on(events, 'removed', (data: { id: string }) => {
    if (subject() === `beep:${data.id}`) void showWhatReloadShows();
    removeBeep(data.id);
  });
  on(events, 'authors_removed', (data: { ids: string[] }) => {
    if (data.ids.some((id) => subject() === `actor:${id}`)) void showWhatReloadShows();
    // A page about a beep (its thread, reporting it, replying to it) goes with its author.
    else if (data.ids.some((id) => subjectCard()?.dataset.author === id)) schedule(checkSubject);
    data.ids.forEach(removeAuthor);
    // Read entries about them go too, which no unread count announces.
    inboxChanged();
  });
  on(events, 'counts', (data: CountsEntry[]) => data.forEach(showCounts));
  on(events, 'liked', (data: { id: string; liked: boolean }) => {
    for (const card of cards(data.id)) {
      const form = card.querySelector<HTMLFormElement>('form[data-like]');
      if (form) showLiked(form, data.liked);
    }
  });
  on(events, 'changed', (data: { ids: string[] }) => redraw(data.ids));
  on(events, 'actor', (data: { id: string }) => {
    redraw(actorCards(data.id));
    refreshRegionsOf(data.id);
    // A thread page's title names the author of its beep.
    const about = subject();
    if (about?.startsWith('beep:') && document.querySelector(`[data-beep="${CSS.escape(about.slice(5))}"][data-author="${CSS.escape(data.id)}"]`)) schedule(refreshTitle);
    if (document.querySelector(`[data-notification][data-from="${CSS.escape(data.id)}"]`)) inboxChanged();
  });
  on(events, 'agents', (data: { id: string }) => {
    refreshRegionsOf(data.id);
    schedule(checkSlots);
  });
  on(events, 'agent_connected', (data: { id: string }) => refreshRegionsOf(data.id));
  on(events, 'resync', () => {
    // Showing what was gone (an error page): it may be back (unblocked, unbanned, restored).
    if (Number(document.documentElement.dataset.liveStatus) >= 400) void showWhatReloadShows();
    else {
      // Blocking either way hides a beep, with no event about the beep itself.
      if (subject()?.startsWith('beep:')) schedule(checkSubject, SUBJECT_INTERVAL);
      resync();
    }
  });
  on(events, 'search', () => refreshRegionNamed('search-results', SEARCH_INTERVAL));
  on(events, 'admin', () => {
    refreshRegionNamed('admin', ADMIN_INTERVAL);
    schedule(checkSlots, ADMIN_INTERVAL);
  });
  // The session ended elsewhere, or the account's language or rights changed: the page as a whole is different now.
  on(events, 'signed_out', () => void showAnew(events));
  on(events, 'reload', () => void showAnew(events));
  // Events were missed: anything may have changed, so everything shown is fetched again.
  on(events, 'reset', () => {
    // From here on the page catches up by fetching; a later reconnect replays from now, not from the gap again.
    lastEventId = undefined;
    document.body.dataset.liveFrom = String(Date.now());
    resync();
    redraw(pageBeepIds());
    // A sign-out may be among the missed events, too.
    void checkSignedIn();
  });
  on(events, 'close', () => {
    closed = true;
    pushedOut = true;
    events.close();
  });
  on(events, 'ping', () => {});
}

// Messages that say nothing about the site's content.
const NOT_CONTENT = new Set(['ping', 'inbox', 'close', 'signed_out', 'reload']);

function on<T>(events: EventSource, type: string, handle: (data: T) => void): void {
  events.addEventListener(type, (event) => {
    if (event.lastEventId) lastEventId = event.lastEventId;
    // The admin pages show figures and lists about everything: whatever changed may be among them.
    if (topic() === 'admin' && !NOT_CONTENT.has(type)) refreshRegionNamed('admin', ADMIN_INTERVAL);
    try {
      handle(JSON.parse(event.data) as T);
    } catch {
      // A malformed event only means one missed update.
    }
  });
}

/**
 * After a failed connection: still signed in as the page says? If not (signed out elsewhere while disconnected), show
 * the page as it now is. If so, the server also says when this account may connect again (connections per minute
 * are limited): the tab reconnects right then, rather than when its backoff happens to try.
 */
async function checkSignedIn(): Promise<void> {
  let state: string;
  let wait: number;
  try {
    const response = await fetch(AUTH_STATE, { credentials: 'same-origin', cache: 'no-store' });
    if (!response.ok) return;
    state = await response.text();
    wait = Number(response.headers.get('X-Stream-Wait-Ms') ?? 'NaN');
  } catch {
    // Offline: the next failed reconnect asks again.
    return;
  }
  if (state !== (document.body.dataset.auth ?? 'anonymous')) {
    closed = true;
    void showWhatReloadShows();
    return;
  }
  // Refused for the connection limit: come back right when it allows, spread a little so tabs do not all come back
  // at once. Anything else keeps the usual backoff.
  if (source || closed || !(wait > 0)) return;
  window.clearTimeout(retryTimer);
  retryDelay = wait + Math.round(Math.random() * 500);
  retryTimer = window.setTimeout(connect, retryDelay);
}

async function refreshTitle(): Promise<void> {
  const response = await fetch(pageUrl(), { credentials: 'same-origin', headers: { Accept: 'text/html' } });
  if (!response.ok && retryable(new HttpError(response.status))) throw new HttpError(response.status);
  if (response.ok) document.title = new DOMParser().parseFromString(await response.text(), 'text/html').title;
}

/**
 * One-time content (a new key, a new invite) stays only while it is still good: each slot names a page that lists
 * what it shows and what to find there; when that is gone (key regenerated, agent deleted, invite revoked), so is it.
 */
async function checkSlots(): Promise<void> {
  const slots = [...document.querySelectorAll<HTMLElement>('[data-live-slot][data-live-slot-check][data-live-slot-requires]')]
    .filter((slot) => slot.dataset.liveSlotRequires && slot.textContent?.trim());
  const pages = new Map<string, Promise<Document | null>>();
  for (const slot of slots) {
    const check = slot.dataset.liveSlotCheck ?? '';
    if (!pages.has(check)) {
      pages.set(check, fetch(check, { credentials: 'same-origin', headers: { Accept: 'text/html' } }).then(async (response) => {
        if (!response.ok) {
          if (retryable(new HttpError(response.status))) throw new HttpError(response.status);
          return null;
        }
        return new DOMParser().parseFromString(await response.text(), 'text/html');
      }));
    }
    const fresh = await pages.get(check);
    if (fresh && !fresh.querySelector(slot.dataset.liveSlotRequires ?? '')) slot.replaceChildren();
  }
}

/**
 * Is what the page is about still there for this viewer? Fetched as a reload would, without showing it: only when it
 * is gone (not found, or the page now leads elsewhere) is the page shown anew, so a half-written reply stays otherwise.
 */
async function checkSubject(): Promise<void> {
  const url = pageUrl();
  const response = await fetch(url, { credentials: 'same-origin', headers: { Accept: 'text/html' } });
  if (!response.ok && retryable(new HttpError(response.status))) throw new HttpError(response.status);
  if (!response.ok || (response.url && response.url !== url)) await showWhatReloadShows();
}

/** The card of the beep the page is about, when it is about one. */
function subjectCard(): HTMLElement | null {
  const about = subject();
  return about?.startsWith('beep:') ? document.querySelector<HTMLElement>(`[data-beep="${CSS.escape(about.slice(5))}"]`) : null;
}

function subject(): string | undefined {
  return document.querySelector<HTMLElement>('[data-live-subject]')?.dataset.liveSubject;
}

/**
 * The page as a whole is not what it was (what it is about is gone, or came back): show what a reload would — not
 * found, the page again, or where the server sends instead.
 */
async function showWhatReloadShows(): Promise<void> {
  try {
    await swapPage();
  } catch {
    window.setTimeout(() => void showWhatReloadShows(), 5000);
  }
}

/** The inbox changed: the inbox page follows it, an older page of the inbox draws its list again. */
function inboxChanged(): void {
  if (topic() === 'notifications') schedule(resyncNotifications);
  else refreshRegionNamed('notifications', 0);
}

async function showAnew(events: EventSource): Promise<void> {
  events.close();
  if (source === events) source = undefined;
  // This tab is going to another page already (it posted the sign-out, say): that page shows it.
  if (leaving) return;
  closed = true;
  try {
    await swapPage();
  } catch {
    // Offline for a moment: try again, the page must not stay as it was.
    window.setTimeout(() => void showAnew(events), 5000);
  }
}

/** A fresh page (swapped in here, or by another tab signing in or out): it names what it shows, as on a first load. */
function followNewPage(): void {
  disconnect();
  closed = false;
  lastEventId = undefined;
  retryDelay = 0;
  changedBeeps.clear();
  connect();
}

function disconnect(): void {
  window.clearTimeout(retryTimer);
  source?.close();
  source = undefined;
}

/**
 * Beeps that came onto the page without the server announcing them (a redraw, catching up) get no counts or
 * removals until the connection lists them: reconnect, which replays from the last event, so nothing is lost.
 */
function followNewBeeps(): void {
  showEmptyStates();
  if (!source || closed || pageBeepIds().every((id) => known.has(id))) return;
  schedule(reconnect);
}

async function reconnect(): Promise<void> {
  if (!source || closed) return;
  disconnect();
  connect();
}

// ---- What the events change ------------------------------------------------------------------

function showUnread(count: number): void {
  for (const badge of document.querySelectorAll<HTMLElement>('[data-inbox-count]')) {
    badge.textContent = count > 99 ? '99+' : String(count);
    badge.hidden = count <= 0;
  }
}

function showCounts(entry: CountsEntry): void {
  for (const beep of cards(entry.id)) {
    const likes = beep.querySelector('[data-like-count]');
    const replies = beep.querySelector('[data-reply-count]');
    // Nothing is left blank, as the server draws it.
    if (likes) likes.textContent = entry.like_count > 0 ? String(entry.like_count) : '';
    if (replies) replies.textContent = entry.reply_count > 0 ? String(entry.reply_count) : '';
  }
}

/** In the inbox the whole entry goes: it is about this beep. */
function removeCard(beep: HTMLElement): void {
  const entry = beep.closest<HTMLElement>('[data-notification]');
  const list = beep.parentElement;
  (entry ?? beep).remove();
  // A reload would show the next ones in its place: the inbox's first page is fetched again, an older page of a
  // list or the inbox drawn again. A live list that grew is longer than a reload anyway; once its last one goes, it
  // is filled from what is left.
  if (entry) inboxChanged();
  else if (list?.matches('.beep-list')) {
    if (!beepPage()) refreshRegionNamed('list', 0);
    else if (!list.querySelector(':scope > [data-beep]')) schedule(resyncBeeps);
  }
  showEmptyStates();
}

function removeBeep(id: string): void {
  cards(id).forEach(removeCard);
}

/** A banned or deleted account: every card by it goes, in timelines, threads and the inbox alike. */
function removeAuthor(id: string): void {
  document.querySelectorAll<HTMLElement>(`[data-beep][data-author="${CSS.escape(id)}"]`).forEach(removeCard);
}

/** Cards that show this actor: written by them, mentioning them, or answering one of theirs ("replying to @…"). */
function actorCards(id: string): string[] {
  const escaped = CSS.escape(id);
  const theirs = [...document.querySelectorAll<HTMLElement>(`[data-beep][data-author="${escaped}"]`)].map((e) => e.dataset.beep ?? '');
  const answering = theirs.flatMap((beep) => [...document.querySelectorAll<HTMLElement>(`[data-beep][data-reply-to="${CSS.escape(beep)}"]`)]);
  return [
    ...theirs,
    ...[...document.querySelectorAll<HTMLElement>(`[data-beep][data-mentions~="${escaped}"]`)].map((e) => e.dataset.beep ?? ''),
    ...answering.map((e) => e.dataset.beep ?? ''),
  ];
}

function redraw(ids: readonly string[]): void {
  let any = false;
  for (const id of ids) {
    if (id && cards(id).length > 0) {
      changedBeeps.add(id);
      any = true;
    }
  }
  if (any) schedule(redrawBeeps);
}

function refreshRegionsOf(actorId: string): void {
  changedActors.add(actorId);
  schedule(refreshRegions);
}

function refreshRegionNamed(name: string, minInterval: number): void {
  if (!document.querySelector(`[data-live-region="${CSS.escape(name)}"]`)) return;
  changedRegions.add(name);
  schedule(refreshRegions, minInterval);
}

/** Something changed that no single event describes: the list, the inbox and every region catch up. */
function resync(): void {
  schedule(resyncBeeps);
  if (topic() === 'notifications') {
    inboxUnannounced = true;
    schedule(resyncNotifications);
  }
  allRegions = true;
  schedule(refreshRegions);
}

/** A fetch the server answered with an error. */
class HttpError extends Error {
  readonly status: number;

  constructor(status: number) {
    super(String(status));
    this.status = status;
  }
}

/**
 * Worth trying again: the network or the server failed (an instance going away, say), not the request. The event that
 * asked for the fetch has been counted as seen, so giving up would lose what it announced for good.
 */
function retryable(error: unknown): boolean {
  return error instanceof TypeError || (error instanceof HttpError && (error.status >= 500 || error.status === 429));
}

const MAX_FETCH_RETRY = 30_000;

const scheduled = new Map<() => Promise<void>, { timer?: number; running: boolean; again: boolean; started: number; failures: number }>();

/**
 * Runs `run` soon, once for any number of calls; calls made while it runs make it run once more. With
 * `minInterval`, not sooner than that after it last started. When it fails for a reason worth retrying, it runs
 * again, waiting longer each time.
 */
function schedule(run: () => Promise<void>, minInterval = 0): void {
  let state = scheduled.get(run);
  if (!state) {
    state = { running: false, again: false, started: 0, failures: 0 };
    scheduled.set(run, state);
  }
  if (state.running) {
    state.again = true;
    return;
  }
  if (state.timer !== undefined) return;
  const current = state;
  const delay = Math.max(FETCH_DELAY, current.started + minInterval - Date.now());
  current.timer = window.setTimeout(() => {
    current.timer = undefined;
    current.running = true;
    current.started = Date.now();
    let retry = 0;
    run()
      .then(
        () => {
          current.failures = 0;
        },
        (error: unknown) => {
          if (!retryable(error)) return;
          current.failures++;
          retry = Math.min(MAX_FETCH_RETRY, 1000 * 2 ** (current.failures - 1));
        },
      )
      .finally(() => {
        current.running = false;
        if (current.again || retry > 0) {
          current.again = false;
          schedule(run, retry);
        }
      });
  }, delay);
}

async function fetchHtml(path: string, params: URLSearchParams): Promise<HTMLTemplateElement> {
  const response = await fetch(`${path}?${params}`, { credentials: 'same-origin', headers: { Accept: 'text/html' } });
  if (!response.ok) throw new HttpError(response.status);
  const template = document.createElement('template');
  template.innerHTML = await response.text();
  return template;
}

/** The top-level elements of a fetched fragment that match (`:scope` does not work on a fragment). */
function topLevel(template: HTMLTemplateElement, selector: string): HTMLElement[] {
  return [...template.content.children].filter((e): e is HTMLElement => e instanceof HTMLElement && e.matches(selector));
}

function freshBeeps(template: HTMLTemplateElement): HTMLElement[] {
  return topLevel(template, '[data-beep]');
}

/** The list new items go into; created after the empty state (hidden from then on) when the page had none. */
function listIn(container: HTMLElement, selector: string, create: () => HTMLElement): HTMLElement {
  const existing = container.querySelector<HTMLElement>(selector);
  if (existing) return existing;
  const list = create();
  const empty = container.querySelector<HTMLElement>(':scope > [data-live-empty], :scope > [data-live-region] > [data-live-empty]');
  if (empty) empty.after(list);
  else container.append(list);
  showEmptyStates();
  return list;
}

/** Each live list's empty state shows exactly while the list has nothing in it, as a reload would. */
function showEmptyStates(): void {
  for (const empty of document.querySelectorAll<HTMLElement>('[data-live-empty]')) {
    empty.hidden = !!empty.parentElement?.querySelector(':scope > .beep-list > [data-beep], :scope > .notification-list > [data-notification]');
  }
}

interface BeepPage {
  container: HTMLElement;
  thread: boolean;
  current: string;
}

/** Topics whose page lists beeps and adds them as they come (LiveTopic.IsBeepList, and threads). */
const LIST_TOPIC = /^(home|public|profile:|replies:|tag:|thread:)/;

/** The page's list of beeps, when it adds beeps as they come: a timeline (newest first) or a thread (oldest first). */
function beepPage(): BeepPage | null {
  const container = watch();
  const current = topic();
  if (!container || !LIST_TOPIC.test(current)) return null;
  return { container, thread: current.startsWith('thread:'), current };
}

/** Created when the first beep goes in, in place of the empty state. */
function beepList(page: BeepPage): HTMLElement {
  return listIn(page.container, '.beep-list', () => Object.assign(document.createElement('div'), { className: 'beep-list' }));
}

function listed(page: BeepPage): HTMLElement[] {
  return [...page.container.querySelectorAll<HTMLElement>('.beep-list > [data-beep]')];
}

function beepParams(current: string): URLSearchParams {
  const params = new URLSearchParams({ watch: current, return: window.location.pathname + window.location.search });
  const focus = document.querySelector<HTMLElement>('[data-beep].beep-focused')?.dataset.beep;
  if (focus) params.set('focus', focus);
  return params;
}

// A burst bigger than this many answers is left for a resync.
const MAX_FETCH_ROUNDS = 20;

async function fetchBeeps(): Promise<void> {
  const page = beepPage();
  if (!page) return;
  const shown = listed(page);
  const params = beepParams(page.current);
  const isNew = (beep: HTMLElement): boolean => cards(beep.dataset.beep ?? '').length === 0;
  if (page.thread) {
    // A thread grows at the bottom, oldest first: ask after the last one until all of it came.
    let after = shown.at(-1)?.dataset.beep ?? NO_ID;
    for (let round = 0; round < MAX_FETCH_ROUNDS; round++) {
      params.set('after', after);
      const fresh = freshBeeps(await fetchHtml(LIVE_BEEPS, params));
      const added = fresh.filter(isNew);
      if (added.length > 0) beepList(page).append(...added);
      if (fresh.length < FETCH_LIMIT) break;
      after = fresh.at(-1)?.dataset.beep ?? after;
    }
  } else {
    // A list grows at the top, newest first. An answer holds the newest ones; a full answer may leave some out
    // between them and what the page showed, so the rest are asked for below the oldest that came.
    params.set('after', shown[0]?.dataset.beep ?? NO_ID);
    let below: HTMLElement | undefined;
    for (let round = 0; round < MAX_FETCH_ROUNDS; round++) {
      const fresh = freshBeeps(await fetchHtml(LIVE_BEEPS, params));
      const added = fresh.filter(isNew);
      if (added.length > 0) {
        if (below) below.after(...added);
        else beepList(page).prepend(...added);
      }
      const oldest = fresh.at(-1)?.dataset.beep;
      if (fresh.length < FETCH_LIMIT || !oldest) break;
      below = cards(oldest)[0];
      params.set('before', oldest);
    }
  }
  followNewBeeps();
}

/**
 * Brings the list up to date with what a reload would show, as far as one fetch reaches: beeps that came back
 * are put in their place, ones the viewer no longer sees go, and the rest are drawn again.
 */
async function resyncBeeps(): Promise<void> {
  const page = beepPage();
  if (!page) return;
  const shown = listed(page);
  // From where the thread page starts, or from the oldest beep a timeline shows (newer ones come newest first).
  const oldest = shown.at(-1)?.dataset.beep;
  const after = page.thread
    ? new URLSearchParams(window.location.search).get('after') ?? NO_ID
    : oldest ? previousId(oldest) : NO_ID;
  const params = beepParams(page.current);
  params.set('after', after);
  const fresh = freshBeeps(await fetchHtml(LIVE_BEEPS, params));
  const ids = fresh.map((beep) => beep.dataset.beep ?? '');
  // A full answer stops somewhere: past that point the page is left as it is.
  const full = fresh.length >= FETCH_LIMIT;
  const edge = ids.at(-1);
  const inRange = (id: string): boolean =>
    compareIds(id, after) > 0 && (!full || !edge || (page.thread ? compareIds(id, edge) <= 0 : compareIds(id, edge) >= 0));
  const wanted = new Map(fresh.map((beep) => [beep.dataset.beep ?? '', beep]));
  for (const card of shown) {
    const id = card.dataset.beep ?? '';
    if (!inRange(id)) continue;
    const replacement = wanted.get(id);
    if (replacement) {
      card.replaceWith(replacement);
      wanted.delete(id);
    } else {
      card.remove();
    }
  }
  // Newest first in a timeline, oldest first in a thread.
  const before = (a: string, b: string): boolean => (page.thread ? compareIds(a, b) < 0 : compareIds(a, b) > 0);
  for (const [id, beep] of wanted) {
    if (cards(id).length > 0) continue;
    const next = listed(page).find((card) => before(id, card.dataset.beep ?? ''));
    if (next) next.before(beep);
    else beepList(page).append(beep);
  }
  followNewBeeps();
}

/** Draws the changed beeps again, wherever the page shows them; the ones the viewer may no longer see go. */
async function redrawBeeps(): Promise<void> {
  const ids = mergeIds([...changedBeeps].filter((id) => cards(id).length > 0));
  ids.forEach((id) => changedBeeps.delete(id));
  if (ids.length === 0) return;
  const params = beepParams(topic());
  params.set('ids', ids.join(','));
  let fetched: HTMLTemplateElement;
  try {
    fetched = await fetchHtml(LIVE_BEEPS, params);
  } catch (error) {
    // Still to do when it is tried again.
    ids.forEach((id) => changedBeeps.add(id));
    throw error;
  }
  const fresh = new Map(freshBeeps(fetched).map((beep) => [beep.dataset.beep ?? '', beep]));
  for (const id of ids) {
    const replacement = fresh.get(id);
    for (const card of cards(id)) {
      if (replacement) card.replaceWith(replacement.cloneNode(true));
      else removeCard(card);
    }
  }
  // More changed than one request carries: the rest go next.
  if (changedBeeps.size > 0) schedule(redrawBeeps);
  followNewBeeps();
}

/** The inbox's first page as it is now: new entries, entries read in another tab, entries gone with their beep. */
async function resyncNotifications(): Promise<void> {
  const container = watch();
  if (!container) return;
  const entries = [...container.querySelectorAll<HTMLElement>('.notification-list > [data-notification]')];
  const newest = entries[0]?.dataset.notification ?? NO_ID;
  const announced = !inboxUnannounced;
  inboxUnannounced = false;
  const limit = Math.min(Math.max(entries.length, INBOX_PAGE), INBOX_MAX);
  let fetched: HTMLTemplateElement;
  try {
    fetched = await fetchHtml(LIVE_NOTIFICATIONS, new URLSearchParams({ limit: String(limit) }));
  } catch (error) {
    inboxUnannounced ||= !announced;
    throw error;
  }
  const fresh = topLevel(fetched, '[data-notification]');
  // Still nothing: the empty state stays.
  if (fresh.length === 0 && entries.length === 0) return;
  const list = listIn(container, '.notification-list', () => Object.assign(document.createElement('ol'), { className: 'notification-list' }));

  list.replaceChildren(...fresh);
  // The inbox events that brought new entries named their beeps, and the server watches those for this page.
  if (announced) {
    for (const entry of fresh.filter((e) => compareIds(e.dataset.notification ?? '', newest) > 0)) {
      entry.querySelectorAll<HTMLElement>('[data-beep]').forEach((beep) => known.add(beep.dataset.beep ?? ''));
    }
  }
  const form = document.querySelector<HTMLFormElement>('form[data-mark-read]');
  const upTo = form?.querySelector<HTMLInputElement>('input[name="upToId"]');
  if (form && upTo) {
    upTo.value = fresh[0]?.dataset.notification ?? upTo.value;
    form.hidden = !list.querySelector('.notification-unread');
  }
  followNewBeeps();
}

/**
 * Draws again the regions that show the changed actors (or all of them), from a fresh copy of the page. Only their
 * content is swapped, so a region stays where it is, and an aria-live region announces what changed.
 */
async function refreshRegions(): Promise<void> {
  const everything = allRegions;
  const actors = new Set(changedActors);
  const named = new Set(changedRegions);
  allRegions = false;
  changedActors.clear();
  changedRegions.clear();
  // A list or an inbox that grows live catches up its own way (resyncBeeps, resyncNotifications).
  const ownWay = (name: string): boolean => (name === 'list' && beepPage() !== null) || (name === 'notifications' && topic() === 'notifications');
  const regions = [...document.querySelectorAll<HTMLElement>('[data-live-region]')].filter((region) => {
    const name = region.dataset.liveRegion ?? '';
    return !ownWay(name) && (everything || named.has(name) || (region.dataset.liveActors ?? '').split(' ').some((id) => actors.has(id)));
  });
  if (regions.length === 0) return;
  // A page whose own address may change (a profile after a rename) names where it can be found by id.
  const source = document.querySelector<HTMLElement>('[data-live-url]')?.dataset.liveUrl;
  let response: Response;
  try {
    response = await fetch(source ? source + window.location.search : pageUrl(), { credentials: 'same-origin', headers: { Accept: 'text/html' } });
    if (!response.ok && retryable(new HttpError(response.status))) throw new HttpError(response.status);
  } catch (error) {
    // Tried again with everything that was asked for.
    allRegions ||= everything;
    actors.forEach((id) => changedActors.add(id));
    named.forEach((name) => changedRegions.add(name));
    throw error;
  }
  if (!response.ok) {
    // The page itself is gone for this person now (the beep or person it is about hidden from them, say): a reload
    // would show that, so the whole page shows it.
    void showWhatReloadShows();
    return;
  }
  const fresh = new DOMParser().parseFromString(await response.text(), 'text/html');
  if (source && response.url) {
    const found = new URL(response.url);
    if (found.pathname !== window.location.pathname) window.history.replaceState(null, '', found.pathname + found.search + window.location.hash);
  }
  document.title = fresh.title;
  for (const region of regions) {
    const replacement = fresh.querySelector<HTMLElement>(`[data-live-region="${CSS.escape(region.dataset.liveRegion ?? '')}"]`);
    if (!replacement) continue;
    swapRegion(region, replacement);
    // Who the region shows may have changed too (an agent added to a list).
    if (replacement.dataset.liveActors === undefined) delete region.dataset.liveActors;
    else region.dataset.liveActors = replacement.dataset.liveActors;
  }
  followNewBeeps();
}

/**
 * Puts the fresh content in, keeping what only this page has: one-time content in slots, and every form someone is
 * filling in (the very element, with what they typed and where the cursor was) in place of its fresh copy.
 */
function swapRegion(region: HTMLElement, replacement: HTMLElement): void {
  const holder = document.createElement('div');
  holder.append(...[...replacement.childNodes].map((node) => document.importNode(node, true)));
  const active = document.activeElement;
  const selection = active instanceof HTMLInputElement || active instanceof HTMLTextAreaElement ? [active.selectionStart, active.selectionEnd] : null;
  for (const [form, key] of formsIn(region)) {
    if (!beingFilledIn(form)) continue;
    const counterpart = formsIn(holder).find(([, other]) => other === key)?.[0];
    counterpart?.replaceWith(form);
  }
  const kept = takeSlots(region);
  region.replaceChildren(...holder.childNodes);
  putSlots(region, kept);
  if (active instanceof HTMLElement && active !== document.activeElement && region.contains(active)) {
    active.focus();
    if (selection && (active instanceof HTMLInputElement || active instanceof HTMLTextAreaElement)) {
      try {
        active.setSelectionRange(selection[0] ?? null, selection[1] ?? null);
      } catch {
        // Some inputs (file, radio) have no selection.
      }
    }
  }
}

/** The forms under `root`, each with a key that finds its counterpart in a fresh copy: its address, and which one of those. */
function formsIn(root: HTMLElement): Array<[HTMLFormElement, string]> {
  const seen = new Map<string, number>();
  return [...root.querySelectorAll('form')].map((form) => {
    const action = form.getAttribute('action') ?? '';
    const index = seen.get(action) ?? 0;
    seen.set(action, index + 1);
    return [form, `${action}#${index}`];
  });
}

/** A form the person is typing in or has changed: drawing it again would lose their input. */
function beingFilledIn(form: HTMLFormElement): boolean {
  const active = document.activeElement;
  return form.hasAttribute('data-live-edited')
    || (active instanceof HTMLElement && form.contains(active) && active.matches('input, textarea, select'));
}

// ---- Start -------------------------------------------------------------------------------------

// What someone types into a form keeps that form as it is when the page around it is drawn again.
document.addEventListener('input', (event) => {
  if (event.target instanceof Element) event.target.closest('form')?.setAttribute('data-live-edited', '');
});
// A tab pushed out by the account's newer ones follows again as soon as it is looked at.
const comeBack = (): void => {
  if (!pushedOut || document.visibilityState !== 'visible') return;
  pushedOut = false;
  followNewPage();
};
document.addEventListener('visibilitychange', comeBack);
window.addEventListener('focus', comeBack);

connect();
// An open connection keeps the page out of the back/forward cache; reconnect (and replay) on return.
document.addEventListener(PAGE_SWAPPED, followNewPage);
window.addEventListener('beforeunload', () => {
  leaving = true;
});
window.addEventListener('pagehide', disconnect);
window.addEventListener('pageshow', (event) => {
  leaving = false;
  if (event.persisted && !source && !closed) connect();
});
