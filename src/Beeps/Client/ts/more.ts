// Infinite scroll, for everyone: every paged list loads its next page as it is scrolled to the end. The server draws
//   <… data-more-list="beeps">        the list (its items keyed by data-beep, data-notification or data-key)
//   <nav data-more="beeps"><a href>   the link to its next page, followed as it is without script
// Here the link's page is fetched as the link nears the bottom of the screen (or when it is clicked), its items are
// added to the end of the list, and the link becomes that page's own, or goes on the last one. The address stays
// the page's own; going back to it loads as many pages again and scrolls to where it was left.
// A page that follows its data live (live.ts) hears of what was added through `beeps:more`, and a thread's last page,
// which follows new replies, brings its topic (data-live-watch) along.

import { itemsToAdd, itemsToKeep } from './more-items.ts';
import { PAGE_SWAPPED } from './page-swap.ts';

export const MORE_LOADED = 'beeps:more';

// Where the history entry keeps how many pages each list loaded, and how far down the page was.
const STATE = 'beepsMore';
// Going back loads at most this many pages again before scrolling.
const MAX_RESTORED = 20;
const SAVE_DELAY = 250;

interface Saved {
  pages: Record<string, number>;
  scroll: number;
}

let loaded: Record<string, number> = {};
const loading = new Map<HTMLElement, Promise<boolean>>();
const watched = new Set<HTMLElement>();
let saveTimer: number | undefined;

function key(item: Element): string | undefined {
  if (!(item instanceof HTMLElement)) return undefined;
  return item.dataset.beep ?? item.dataset.notification ?? item.dataset.key;
}

function listNamed(root: ParentNode, name: string): HTMLElement | null {
  return root.querySelector<HTMLElement>(`[data-more-list="${CSS.escape(name)}"]`);
}

function pagerNamed(root: ParentNode, name: string): HTMLElement | null {
  return root.querySelector<HTMLElement>(`[data-more="${CSS.escape(name)}"]`);
}

/** Loads the pager's next page into its list; false when it could not (the link stays, to try again). */
function load(pager: HTMLElement): Promise<boolean> {
  let running = loading.get(pager);
  if (!running) {
    running = fetchNext(pager).finally(() => loading.delete(pager));
    loading.set(pager, running);
  }
  return running;
}

async function fetchNext(pager: HTMLElement): Promise<boolean> {
  const link = pager.querySelector<HTMLAnchorElement>('a[href]');
  const name = pager.dataset.more ?? '';
  if (!link) return false;
  pager.dataset.moreState = 'loading';
  pager.setAttribute('aria-busy', 'true');
  let fresh: Document;
  try {
    const response = await fetch(link.href, { credentials: 'same-origin', headers: { Accept: 'text/html' } });
    if (!response.ok) throw new Error(String(response.status));
    fresh = new DOMParser().parseFromString(await response.text(), 'text/html');
  } catch {
    pager.dataset.moreState = 'failed';
    pager.removeAttribute('aria-busy');
    return false;
  }
  // Found again now: the region around it may have been drawn again meanwhile, keeping this link (live.ts).
  const list = listNamed(document, name);
  if (!pager.isConnected || !list) return false;
  const items = [...(listNamed(fresh, name)?.children ?? [])];
  const shown = new Set([...list.children, ...document.querySelectorAll('[data-beep]')].map(key).filter((k) => k !== undefined));
  list.append(...itemsToAdd(shown, items.map(key)).map((index) => document.importNode(items[index] as Element, true)));
  list.dataset.moreLoaded = '';
  const next = pagerNamed(fresh, name);
  if (next) pager.replaceWith(document.importNode(next, true));
  else pager.remove();
  takeTopic(fresh);
  loaded[name] = (loaded[name] ?? 0) + 1;
  save();
  document.dispatchEvent(new CustomEvent(MORE_LOADED));
  return true;
}

/** A page that follows nothing live yet takes on what the page it loaded follows (a thread's last page, its replies). */
function takeTopic(fresh: Document): void {
  const watch = document.querySelector<HTMLElement>('[data-live-watch]');
  const topic = fresh.querySelector<HTMLElement>('[data-live-watch]')?.dataset.liveWatch;
  if (watch && !watch.dataset.liveWatch && topic) watch.dataset.liveWatch = topic;
}

/**
 * For a region drawn again from a fresh copy of its page (live.ts), into `fresh`: a list that loaded more pages keeps
 * what it loaded past the fresh first page, with the link that goes on from there. A fresh page with no link has it
 * all, so nothing is kept then.
 */
export function keepLoaded(region: HTMLElement, fresh: HTMLElement): void {
  for (const list of region.querySelectorAll<HTMLElement>('[data-more-list][data-more-loaded]')) {
    const name = list.dataset.moreList ?? '';
    const freshList = listNamed(fresh, name);
    const freshPager = pagerNamed(fresh, name);
    if (!freshList || !freshPager) continue;
    const items = [...list.children];
    const kept = itemsToKeep(items.map(key), [...freshList.children].map(key)).map((index) => items[index] as Element);
    const pager = pagerNamed(region, name);
    freshList.append(...kept);
    freshList.dataset.moreLoaded = '';
    if (pager) freshPager.replaceWith(pager);
    else freshPager.remove();
  }
}

// ---- Going back ---------------------------------------------------------------------------------

function savedState(): Saved | undefined {
  const state: unknown = window.history.state;
  if (typeof state !== 'object' || state === null || !(STATE in state)) return undefined;
  return (state as Record<string, Saved>)[STATE];
}

function save(): void {
  window.clearTimeout(saveTimer);
  saveTimer = undefined;
  if (Object.keys(loaded).length === 0) return;
  const state: unknown = window.history.state;
  const others = typeof state === 'object' && state !== null ? state : {};
  try {
    window.history.replaceState({ ...others, [STATE]: { pages: loaded, scroll: window.scrollY } satisfies Saved }, '');
    // The browser would scroll before the pages are loaded again.
    window.history.scrollRestoration = 'manual';
  } catch {
    // Too many history changes at once (Safari): the next scroll saves it.
  }
}

/** Back on a page that had loaded more: the same pages again, then where it was left. */
async function restore(): Promise<void> {
  const saved = savedState();
  if (!saved) return;
  for (const [name, count] of Object.entries(saved.pages)) {
    for (let page = 0; page < Math.min(count, MAX_RESTORED); page++) {
      const pager = pagerNamed(document, name);
      if (!pager || !(await load(pager))) break;
    }
  }
  window.scrollTo(0, saved.scroll);
}

// ---- Start --------------------------------------------------------------------------------------

export function initMore(): void {
  const observer = new IntersectionObserver(
    (entries) => {
      for (const entry of entries) {
        if (entry.isIntersecting && entry.target instanceof HTMLElement) void load(entry.target);
      }
    },
    // A screen ahead, so the next page is there before it is scrolled to.
    { rootMargin: '0px 0px 100% 0px' },
  );
  // Links come and go with the pages loaded, and with regions drawn again.
  const watchLinks = (): void => {
    for (const pager of watched) {
      if (pager.isConnected) continue;
      observer.unobserve(pager);
      watched.delete(pager);
    }
    for (const pager of document.querySelectorAll<HTMLElement>('[data-more]')) {
      if (watched.has(pager)) continue;
      watched.add(pager);
      pager.dataset.moreState ??= 'idle';
      observer.observe(pager);
    }
  };
  watchLinks();
  new MutationObserver(watchLinks).observe(document.body, { childList: true, subtree: true });

  document.addEventListener('click', (event) => {
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || !(event.target instanceof Element)) return;
    const pager = event.target.closest<HTMLElement>('[data-more]');
    if (!pager || !event.target.closest('a[href]')) return;
    event.preventDefault();
    void load(pager);
  });
  window.addEventListener('scroll', () => {
    if (Object.keys(loaded).length === 0 || saveTimer !== undefined) return;
    saveTimer = window.setTimeout(save, SAVE_DELAY);
  }, { passive: true });
  window.addEventListener('pagehide', save);
  // A page shown anew (signed in or out) starts again from its first page.
  document.addEventListener(PAGE_SWAPPED, () => {
    loaded = {};
    window.history.scrollRestoration = 'auto';
  });
  void restore();
}
