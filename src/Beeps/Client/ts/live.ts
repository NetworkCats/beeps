// Live updates for signed-in members (PLAN 6.10); the layout loads this file for nobody else. One
// EventSource per tab, on <body data-live-stream>, subscribed to what this page shows:
//   <… data-live-watch="home">     the page's topic (empty: nothing gets added, but counts still update)
//   <article data-beep="…">        beeps whose counts update live and that disappear when deleted
//   [data-inbox-count]             the unread badge
//   [data-live-agent="…"]          reloads when that agent connects (onboarding); [data-live-agents] on any
//   [data-live-region="…"]         swapped for a fresh copy when the profile on the page changes
// New content is never built here: the page fetches it from /live/…, rendered by the server for this
// viewer, so visibility and markup are exactly those of a reload.

import { liveStreamUrl, mergeIds, nextRetryDelay } from './live-url.ts';

const LIVE_BEEPS = '/live/beeps';
const LIVE_NOTIFICATIONS = '/live/notifications';
const NO_ID = '00000000-0000-0000-0000-000000000000';
// Several events in a row lead to one fetch.
const FETCH_DELAY = 300;

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

function watch(): HTMLElement | null {
  return document.querySelector<HTMLElement>('[data-live-watch]');
}

function topic(): string {
  return watch()?.dataset.liveWatch ?? '';
}

function beepIds(): string[] {
  return mergeIds([...document.querySelectorAll<HTMLElement>('[data-beep]')].map((e) => e.dataset.beep ?? ''));
}

function connect(): void {
  const base = document.body.dataset.liveStream;
  if (!base || closed) return;
  const events = new EventSource(liveStreamUrl(base, topic(), beepIds(), lastEventId, document.body.dataset.liveFrom));
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
  });
  on(events, 'inbox', (data: { unread_count: number }) => {
    showUnread(data.unread_count);
    if (topic() === 'notifications') schedule(fetchNotifications);
  });
  on(events, 'beep', (data: { id: string }) => {
    if (!document.querySelector(`[data-beep="${data.id}"]`)) schedule(fetchBeeps);
  });
  on(events, 'removed', (data: { id: string }) => removeBeep(data.id));
  on(events, 'counts', (data: CountsEntry[]) => data.forEach(showCounts));
  on(events, 'agent_connected', (data: { id: string }) => {
    if (document.querySelector(`[data-live-agent="${data.id}"], [data-live-agents]`)) window.location.reload();
  });
  on(events, 'actor', () => void refreshRegions());
  on(events, 'reset', () => {
    schedule(fetchBeeps);
    if (topic() === 'notifications') schedule(fetchNotifications);
  });
  on(events, 'close', () => {
    closed = true;
    events.close();
  });
  on(events, 'ping', () => {});
}

function on<T>(events: EventSource, type: string, handle: (data: T) => void): void {
  events.addEventListener(type, (event) => {
    if (event.lastEventId) lastEventId = event.lastEventId;
    try {
      handle(JSON.parse(event.data) as T);
    } catch {
      // A malformed event only means one missed update.
    }
  });
}

function disconnect(): void {
  window.clearTimeout(retryTimer);
  source?.close();
  source = undefined;
}

// ---- What the events change ------------------------------------------------------------------

function showUnread(count: number): void {
  for (const badge of document.querySelectorAll<HTMLElement>('[data-inbox-count]')) {
    badge.textContent = count > 99 ? '99+' : String(count);
    badge.hidden = count <= 0;
  }
}

function showCounts(entry: CountsEntry): void {
  for (const beep of document.querySelectorAll<HTMLElement>(`[data-beep="${entry.id}"]`)) {
    const likes = beep.querySelector('[data-like-count]');
    const replies = beep.querySelector('[data-reply-count]');
    if (likes) likes.textContent = String(entry.like_count);
    if (replies) replies.textContent = String(entry.reply_count);
  }
}

function removeBeep(id: string): void {
  for (const beep of document.querySelectorAll<HTMLElement>(`[data-beep="${id}"]`)) {
    // In the inbox the whole entry goes: it is about this beep.
    (beep.closest('[data-notification]') ?? beep).remove();
  }
}

const scheduled = new Map<() => Promise<void>, { timer?: number; running: boolean; again: boolean }>();

/** Runs `run` soon, once for any number of calls; calls made while it runs make it run once more. */
function schedule(run: () => Promise<void>): void {
  let state = scheduled.get(run);
  if (!state) {
    state = { running: false, again: false };
    scheduled.set(run, state);
  }
  if (state.running) {
    state.again = true;
    return;
  }
  if (state.timer !== undefined) return;
  const current = state;
  current.timer = window.setTimeout(() => {
    current.timer = undefined;
    current.running = true;
    run()
      .catch(() => {})
      .finally(() => {
        current.running = false;
        if (current.again) {
          current.again = false;
          schedule(run);
        }
      });
  }, FETCH_DELAY);
}

async function fetchHtml(path: string, params: URLSearchParams): Promise<HTMLTemplateElement> {
  const response = await fetch(`${path}?${params}`, { credentials: 'same-origin', headers: { Accept: 'text/html' } });
  if (!response.ok) throw new Error(String(response.status));
  const template = document.createElement('template');
  template.innerHTML = await response.text();
  return template;
}

/** The list new items go into; created in place of the empty state when the page had none. */
function listIn(container: HTMLElement, selector: string, create: () => HTMLElement): HTMLElement {
  const existing = container.querySelector<HTMLElement>(selector);
  if (existing) return existing;
  const list = create();
  const empty = container.querySelector(':scope > .empty');
  if (empty) empty.replaceWith(list);
  else container.append(list);
  return list;
}

async function fetchBeeps(): Promise<void> {
  const container = watch();
  const current = topic();
  if (!container || !current || current === 'notifications') return;
  const thread = current.startsWith('thread:');
  const list = listIn(container, '.beep-list', () => Object.assign(document.createElement('div'), { className: 'beep-list' }));
  const shown = [...list.querySelectorAll<HTMLElement>(':scope > [data-beep]')];
  // A list grows at the top, newest first; a thread at the bottom, oldest first.
  const cursor = (thread ? shown.at(-1) : shown[0])?.dataset.beep ?? NO_ID;
  const params = new URLSearchParams({ watch: current, after: cursor, return: window.location.pathname + window.location.search });
  const fresh = [...(await fetchHtml(LIVE_BEEPS, params)).content.querySelectorAll<HTMLElement>('[data-beep]')]
    .filter((beep) => !document.querySelector(`[data-beep="${beep.dataset.beep}"]`));
  if (thread) list.append(...fresh);
  else list.prepend(...fresh);
}

async function fetchNotifications(): Promise<void> {
  const container = watch();
  if (!container) return;
  const list = listIn(container, '.notification-list', () => Object.assign(document.createElement('ol'), { className: 'notification-list' }));
  const newest = list.querySelector<HTMLElement>(':scope > [data-notification]')?.dataset.notification ?? NO_ID;
  const fresh = [...(await fetchHtml(LIVE_NOTIFICATIONS, new URLSearchParams({ after: newest }))).content.querySelectorAll<HTMLElement>('[data-notification]')]
    .filter((item) => !list.querySelector(`[data-notification="${item.dataset.notification}"]`));
  if (fresh.length === 0) return;
  list.prepend(...fresh);
  const form = document.querySelector<HTMLFormElement>('form[data-mark-read]');
  const upTo = form?.querySelector<HTMLInputElement>('input[name="upToId"]');
  if (form && upTo) {
    upTo.value = fresh[0]?.dataset.notification ?? upTo.value;
    form.hidden = !list.querySelector('.notification-unread');
  }
}

/** Swaps every [data-live-region] for its counterpart in a fresh copy of the page. */
async function refreshRegions(): Promise<void> {
  const regions = document.querySelectorAll<HTMLElement>('[data-live-region]');
  if (regions.length === 0) return;
  const response = await fetch(window.location.href, { credentials: 'same-origin', headers: { Accept: 'text/html' } });
  if (!response.ok) return;
  const fresh = new DOMParser().parseFromString(await response.text(), 'text/html');
  for (const region of regions) {
    const replacement = fresh.querySelector(`[data-live-region="${region.dataset.liveRegion}"]`);
    if (replacement) region.replaceWith(document.importNode(replacement, true));
  }
}

// ---- Start -------------------------------------------------------------------------------------

connect();
// An open connection keeps the page out of the back/forward cache; reconnect (and replay) on return.
window.addEventListener('pagehide', disconnect);
window.addEventListener('pageshow', (event) => {
  if (event.persisted && !source) connect();
});
