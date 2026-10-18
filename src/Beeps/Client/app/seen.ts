// Notifications read by being seen (PLAN 4.3): a mention or a reply is read once its beep has been on screen, on any
// page but the inbox itself, which has its own button for that. The site hears of seen beeps only while something is
// unread, and of each only once, unless the unread count went up since: a notification can come after its beep showed.
import { useCallback, useRef } from 'preact/hooks';
import { viewer } from './config.ts';
import { formData } from './http.ts';
import { paths } from './paths.ts';
import { unread } from './state.ts';

// Seen: half the card on screen, or half the screen for a card taller than that.
const SEEN_SHARE = 0.5;
// Beeps seen together are told together, this long after the first.
const SEND_DELAY = 1000;
// The most the site takes at once (NotificationService.MaxSeenBeeps).
const MAX_SEEN = 100;

const cards = new Map<Element, string>();
const onScreen = new Set<Element>();
const told = new Set<string>();
const waiting = new Set<string>();
let observer: IntersectionObserver | null | undefined;
let timer = 0;
let lastUnread = 0;

function seenEnough(entry: IntersectionObserverEntry): boolean {
  if (!entry.isIntersecting) return false;
  const screen = entry.rootBounds?.height ?? window.innerHeight;
  return entry.intersectionRatio >= SEEN_SHARE || entry.intersectionRect.height >= screen * SEEN_SHARE;
}

function tell(ids: Iterable<string>): void {
  if (unread.value <= 0 || document.visibilityState !== 'visible') return;
  for (const id of ids) if (!told.has(id)) waiting.add(id);
  if (waiting.size > 0 && !timer) timer = window.setTimeout(send, SEND_DELAY);
}

function send(): void {
  timer = 0;
  const ids = [...waiting].slice(0, MAX_SEEN);
  for (const id of ids) {
    waiting.delete(id);
    told.add(id);
  }
  if (waiting.size > 0) timer = window.setTimeout(send, SEND_DELAY);
  // Told again when next seen, if this did not go through.
  const forget = (): void => ids.forEach((id) => told.delete(id));
  fetch(paths.notificationsSeen, { method: 'POST', body: formData({ ids: ids.join(',') }), credentials: 'same-origin', keepalive: true })
    .then((response) => !response.ok && forget())
    .catch(forget);
}

function shownNow(): string[] {
  return [...onScreen].map((card) => cards.get(card) ?? '').filter(Boolean);
}

/** Null where there is nothing to watch with, or no inbox. */
function start(): IntersectionObserver | null {
  if (typeof IntersectionObserver === 'undefined' || !viewer.value.member) return null;
  unread.subscribe((count) => {
    if (count > lastUnread) {
      told.clear();
      tell(shownNow());
    }
    lastUnread = count;
  });
  document.addEventListener('visibilitychange', () => tell(shownNow()));
  return new IntersectionObserver(
    (entries) => {
      const seen: string[] = [];
      for (const entry of entries) {
        if (!cards.has(entry.target)) continue;
        if (!seenEnough(entry)) {
          onScreen.delete(entry.target);
          continue;
        }
        onScreen.add(entry.target);
        seen.push(cards.get(entry.target) ?? '');
      }
      tell(seen);
    },
    { threshold: [0, SEEN_SHARE, 1] },
  );
}

function watch(card: Element, id: string): void {
  if (observer === undefined) observer = start();
  if (!observer) return;
  cards.set(card, id);
  observer.observe(card);
}

function unwatch(card: Element): void {
  observer?.unobserve(card);
  cards.delete(card);
  onScreen.delete(card);
}

/** A ref for the card that shows beep `id`: seeing it reads the notifications it is, unless `watched` is false. */
export function useSeen(id: string, watched: boolean): (card: Element | null) => void {
  const current = useRef<Element | null>(null);
  return useCallback(
    (card: Element | null) => {
      if (current.current) unwatch(current.current);
      current.current = card;
      if (card && watched) watch(card, id);
    },
    [id, watched],
  );
}
