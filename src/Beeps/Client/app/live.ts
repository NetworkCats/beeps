// The live connection (PLAN 6.10): one EventSource per tab, kept as the person moves between pages, subscribed to
// what the page shows now: its topic (the page island's LiveProps.watch) and the beeps its cards show. What the events
// say goes into the beeps every card draws from (beeps.ts), or to the islands that follow it (page.ts).
import { effect } from '@preact/signals';
import { isShown, redraw, removeAuthors, removeBeep, setCounts, setLiked, shownChanged, shownIds, shownWith } from './beeps.ts';
import { viewer, type Viewer } from './config.ts';
import { getJson } from './http.ts';
import { liveStreamUrl, nextRetryDelay } from './live-url.ts';
import { emit, pageLive, refreshPage, showAnew } from './page.ts';
import { PAGE_SHOWN, type PageShown } from './router.ts';
import { schedule } from './schedule.ts';
import { theme, unread } from './state.ts';

const AUTH_STATE = '/stream/auth';
const LIVE_VIEWER = '/live/viewer';
// Search results are asked again at most this often (searching has its own rate limit).
const SEARCH_INTERVAL = 10_000;
// Admin pages: drawn again at most this often. Figures that change with no event of their own (backlogs, dead jobs)
// are watched by the server while an admin page is open (AdminFiguresWorker), which then sends `admin`.
const ADMIN_INTERVAL = 3_000;

let source: EventSource | undefined;
let lastEventId: string | undefined;
let retryDelay = 0;
let retryTimer: number | undefined;
// Ended by the server for good (too many tabs open), or this tab is leaving: do not come back.
let closed = false;
// Pushed out by the account's newer tabs: come back when this one is looked at again (pushing out the oldest).
let pushedOut = false;
// This tab is going to another page already (it posted the sign-out, say): that page is the new one to show.
let leaving = false;
// What the connection was opened for: the topic, and the beeps the server was told of or announced since.
let connectedTopic = '';
let known = new Set<string>();

function topic(): string {
  return pageLive.value.watch ?? '';
}

function subject(): string | undefined {
  return pageLive.value.subject;
}

function connect(): void {
  const base = document.body.dataset.liveStream;
  if (!base || closed) return;
  const shown = shownIds();
  // Beyond what one connection may list, the server sends what concerns any beep: all of them are known.
  known = new Set(shown);
  connectedTopic = topic();
  const events = new EventSource(liveStreamUrl(base, connectedTopic, shown, lastEventId, document.body.dataset.liveFrom));
  source = events;
  events.addEventListener('open', () => {
    retryDelay = 0;
  });
  // The browser would reconnect with the same URL; reconnecting here names the beeps shown since.
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
    unread.value = data.unread_count;
    emit('inbox');
  });
  on(events, 'beep', (data: { id: string }) => {
    known.add(data.id);
    if (!isShown(data.id)) emit('beep', data.id);
  });
  on(events, 'removed', (data: { id: string }) => {
    if (subject() === `beep:${data.id}`) void showAnew();
    removeBeep(data.id);
    emit('removed', data.id);
  });
  on(events, 'authors_removed', (data: { ids: string[] }) => {
    if (data.ids.some((id) => subject() === `actor:${id}`)) void showAnew();
    removeAuthors(data.ids);
    emit('authors_removed', data.ids);
    // Read entries about them go too, which no unread count announces.
    emit('inbox');
  });
  on(events, 'counts', (data: Array<{ id: string; like_count: number; reply_count: number }>) => {
    for (const entry of data) setCounts(entry.id, entry.like_count, entry.reply_count);
  });
  on(events, 'liked', (data: { id: string; liked: boolean }) => setLiked(data.id, data.liked));
  on(events, 'changed', (data: { ids: string[] }) => redraw(data.ids));
  on(events, 'actor', (data: { id: string }) => {
    redraw(shownWith(data.id));
    actorChanged(data.id);
    emit('actor', data.id);
  });
  on(events, 'agents', (data: { id: string }) => {
    actorChanged(data.id);
    emit('agents', data.id);
  });
  on(events, 'agent_connected', (data: { id: string }) => actorChanged(data.id));
  on(events, 'resync', () => {
    // Showing what was gone (an error page): it may be back (unblocked, unbanned, restored).
    if (Number(document.documentElement.dataset.liveStatus) >= 400) {
      void showAnew();
      return;
    }
    resync();
  });
  on(events, 'search', () => {
    if (pageLive.value.on === 'search') refreshPage(SEARCH_INTERVAL);
  });
  on(events, 'admin', () => {
    if (pageLive.value.on === 'admin') refreshPage(ADMIN_INTERVAL);
  });
  on(events, 'sessions', () => {
    if (pageLive.value.on === 'sessions') refreshPage();
  });
  // The account picked another colour scheme, here or in another tab.
  on(events, 'theme', (data: { theme: string }) => {
    theme.value = data.theme;
  });
  // The session ended elsewhere, or the account's language or rights changed: the page as a whole is different now.
  on(events, 'signed_out', () => void showWhole(events));
  on(events, 'reload', () => void showWhole(events));
  // Events were missed: anything may have changed, so everything shown is fetched again.
  on(events, 'reset', (_, moved) => {
    // From here on the page catches up by fetching; a later reconnect replays from where the server caught up (the
    // reset's id), or else from now, not from the gap again.
    if (!moved) {
      lastEventId = undefined;
      document.body.dataset.liveFrom = String(Date.now());
    }
    resync();
    redraw(shownIds());
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
const NOT_CONTENT = new Set(['ping', 'inbox', 'close', 'signed_out', 'reload', 'theme', 'sessions']);

/** `moved`: the message came with an id of its own, which is now where a reconnect starts from. */
function on<T>(events: EventSource, type: string, handle: (data: T, moved: boolean) => void): void {
  events.addEventListener(type, (event) => {
    const before = lastEventId;
    if (event.lastEventId) lastEventId = event.lastEventId;
    // The admin pages show figures and lists about everything: whatever changed may be among them.
    if (pageLive.value.on === 'admin' && !NOT_CONTENT.has(type)) refreshPage(ADMIN_INTERVAL);
    try {
      handle(JSON.parse(event.data) as T, lastEventId !== before);
    } catch {
      // A malformed event only means one missed update.
    }
  });
}

/** An actor changed (profile, avatar, agents): islands that show them catch up, and so does the viewer's own avatar. */
function actorChanged(id: string): void {
  if (pageLive.value.actors?.includes(id)) refreshPage();
  // Only members may ask (/live).
  if (id === viewer.value.id && viewer.value.member) schedule(refreshViewer);
}

/** The viewer as the server has them now: the account menu and the composer show their new avatar. */
async function refreshViewer(): Promise<void> {
  viewer.value = await getJson<Viewer>(LIVE_VIEWER);
}

/** Something changed that no single event describes: the lists, the inbox and every island catch up. */
function resync(): void {
  emit('resync');
  refreshPage();
}

/**
 * After a failed connection: still signed in as the page says? If not (signed out elsewhere while disconnected), show
 * the page as it now is. If so, the server also says when this account may connect again (connections per minute are
 * limited): the tab reconnects right then, rather than when its backoff happens to try.
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
    void showAnew();
    return;
  }
  // Refused for the connection limit: come back right when it allows, spread a little so tabs do not all come back
  // at once. Anything else keeps the usual backoff.
  if (source || closed || !(wait > 0)) return;
  window.clearTimeout(retryTimer);
  retryDelay = wait + Math.round(Math.random() * 500);
  retryTimer = window.setTimeout(connect, retryDelay);
}

async function showWhole(events: EventSource): Promise<void> {
  events.close();
  if (source === events) source = undefined;
  // This tab is going to another page already (it posted the sign-out, say): that page shows it.
  if (leaving) return;
  closed = true;
  await showAnew();
}

function disconnect(): void {
  window.clearTimeout(retryTimer);
  source?.close();
  source = undefined;
}

/** Whether the connection follows less than the page shows: another topic, or beeps the server was not told of. */
function behind(): boolean {
  return topic() !== connectedTopic || shownIds().some((id) => !known.has(id));
}

async function reconnect(): Promise<void> {
  if (!source || closed || !behind()) return;
  disconnect();
  connect();
}

/**
 * Another page is shown: the connection follows it from where it was, so nothing in between is lost. A page fetched
 * ahead may have been drawn before that, and one gone back to was drawn when first shown, so these are followed from
 * when they were drawn instead: the last event this tab saw passed over what concerned them while another page was up
 * (and a page left too long ago is told to fetch everything again).
 */
function followPage(event?: Event): void {
  const detail = (event as CustomEvent<PageShown> | undefined)?.detail;
  if (detail?.ahead || detail?.restored) lastEventId = undefined;
  disconnect();
  closed = false;
  retryDelay = 0;
  connect();
}

export function startLive(): void {
  // What someone types or a list loads brings new beeps the connection was not told of: reconnect, which replays from
  // the last event, so nothing is lost. A thread that loaded its last page now follows its replies.
  effect(() => {
    void shownChanged.value;
    void pageLive.value;
    if (source && !closed && behind()) schedule(reconnect);
  });
  // A tab pushed out by the account's newer ones follows again as soon as it is looked at.
  const comeBack = (): void => {
    if (!pushedOut || document.visibilityState !== 'visible') return;
    pushedOut = false;
    followPage();
  };
  document.addEventListener('visibilitychange', comeBack);
  window.addEventListener('focus', comeBack);
  document.addEventListener(PAGE_SHOWN, followPage);
  window.addEventListener('beforeunload', () => {
    leaving = true;
  });
  // An open connection keeps the page out of the back/forward cache; reconnect (and replay) on return.
  window.addEventListener('pagehide', disconnect);
  window.addEventListener('pageshow', (event) => {
    leaving = false;
    if (!event.persisted) return;
    // Kept as it was being replaced (signed out, say): Back would show the account's page to whoever is here now.
    if (closed && !pushedOut) void showAnew();
    else if (!source && !closed) connect();
  });
  connect();
}
