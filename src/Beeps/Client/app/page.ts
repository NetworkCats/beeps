// The page shown now, as the live connection and its islands see it: what it follows (its page island's LiveProps),
// the live events for it, and catching up its islands from a fresh islands request.
import { signal } from '@preact/signals';
import { useEffect, useLayoutEffect, useRef } from 'preact/hooks';
import { getIslands, HttpError } from './http.ts';
import { applyIslands } from './islands.tsx';
import type { IslandsPage, LiveProps } from './props.ts';
import { pageUrl, reload } from './router.ts';
import { schedule } from './schedule.ts';
import { showToast } from './toasts.ts';

/** What the page island follows; the live connection subscribes to its topic. */
export const pageLive = signal<LiveProps>({});

/** The page island says what it follows, for as long as it is shown. */
export function useLive(live: LiveProps | undefined): void {
  const key = JSON.stringify(live ?? {});
  // Before the page is shown, so the live connection opened for it follows it from the start.
  useLayoutEffect(() => {
    const value = JSON.parse(key) as LiveProps;
    pageLive.value = value;
    return () => {
      if (pageLive.value === value) pageLive.value = {};
    };
  }, [key]);
}

// ---- Live events, for the islands -------------------------------------------------------------

const bus = new EventTarget();

/** Tells the islands of something that happened (the live connection's events, and a few of the app's own). */
export function emit(type: string, detail?: unknown): void {
  bus.dispatchEvent(new CustomEvent(type, { detail }));
}

/** Calls `handle` for each `type` event while the component is shown. */
export function useLiveEvent<T = unknown>(type: string, handle: (detail: T) => void): void {
  const latest = useRef(handle);
  latest.current = handle;
  useEffect(() => {
    const listener = (event: Event): void => latest.current((event as CustomEvent<T>).detail);
    bus.addEventListener(type, listener);
    return () => bus.removeEventListener(type, listener);
  }, [type]);
}

// ---- Catching up -------------------------------------------------------------------------------

/** Shows the page as a whole anew (what it is about is gone, say): what a reload shows. Tried again until it works. */
export async function showAnew(): Promise<void> {
  try {
    await reload();
  } catch {
    window.setTimeout(() => void showAnew(), 5000);
  }
}

/**
 * What an islands request came back with, shown: the islands' new props, the page's title and notices. Ending up on
 * another page (a form that leads elsewhere, a profile renamed) or on none (an error) shows that page instead.
 */
export function showAnswer(answer: { url: string; status: number; page: IslandsPage | null }, sameAddress: (url: URL) => boolean): boolean {
  const page = answer.page;
  if (!page || (answer.status >= 400 && Object.keys(page.islands).length === 0)) return false;
  if (answer.url && !sameAddress(new URL(answer.url))) return false;
  document.title = page.title;
  applyIslands(page);
  if (page.notice) showToast(page.notice);
  if (page.alert) showToast(page.alert, true);
  return true;
}

async function refresh(): Promise<void> {
  const live = pageLive.value;
  const page = pageUrl();
  // A page whose own address may change (a profile after a rename) says where it can be found by id.
  const url = live.url ? live.url + window.location.search : page;
  const answer = await getIslands(url);
  // Another page was shown meanwhile (or this one with another query): the answer is for the one left.
  if (pageUrl() !== page || pageLive.value !== live) return;
  if (answer.status >= 500) throw new HttpError(answer.status);
  const shownHere = showAnswer(answer, (found) => {
    if (!live.url) return found.pathname === window.location.pathname;
    if (found.pathname !== window.location.pathname) {
      window.history.replaceState(window.history.state, '', found.pathname + found.search + window.location.hash);
    }
    return true;
  });
  // The page itself is gone for this person now (hidden from them, say): a reload would show that.
  if (!shownHere) await showAnew();
}

// A refresh asked for while the tab was in the background, made once it is looked at again.
let refreshWhenShown = false;

function refreshOnceShown(): void {
  if (document.visibilityState === 'hidden') return;
  document.removeEventListener('visibilitychange', refreshOnceShown);
  refreshWhenShown = false;
  schedule(refresh);
}

/**
 * Fetches the page's islands again, soon; with `minInterval`, not sooner than that after the last time. A tab in the
 * background does it once it is shown again, not for every change meanwhile (open search tabs would otherwise ask
 * again every few seconds while the site is busy).
 */
export function refreshPage(minInterval = 0): void {
  if (document.visibilityState === 'hidden') {
    if (!refreshWhenShown) {
      refreshWhenShown = true;
      document.addEventListener('visibilitychange', refreshOnceShown);
    }
    return;
  }
  schedule(refresh, minInterval);
}
