// How a list that loaded more pages, or grew live, takes in a fresh copy of its first page. No browser here, so Node
// can test it.
import { compareIds } from './live-url.ts';

interface ListState {
  ids: string[];
  next?: string;
}

/**
 * The list drawn again from a fresh first page: those shown past the last one the fresh page has stay (pushed off the
 * first page by new ones, or loaded as the list was scrolled), with the next page from where they end; the rest is the
 * fresh page as it is. A fresh page that has none of what was shown keeps everything below it; an empty one is empty.
 */
export function mergeFresh(shown: ListState, fresh: ListState): ListState {
  if (fresh.ids.length === 0) return { ids: [], next: fresh.next };
  const freshIds = new Set(fresh.ids);
  const last = fresh.ids.at(-1) ?? '';
  const at = shown.ids.indexOf(last);
  const kept = shown.ids.slice(at + 1).filter((id) => !freshIds.has(id));
  return kept.length > 0 ? { ids: [...fresh.ids, ...kept], next: shown.next } : fresh;
}

/** A page loaded as the list was scrolled, added at its end: all but what the list shows already. */
export function appendPage(shown: ListState, page: ListState): ListState {
  const ids = new Set(shown.ids);
  return { ids: [...shown.ids, ...page.ids.filter((id) => !ids.has(id))], next: page.next };
}

/**
 * New beeps put in their place: a timeline is newest first, a thread oldest first. Ones already there stay where
 * they are.
 */
export function insertOrdered(ids: readonly string[], added: readonly string[], thread: boolean): string[] {
  const result = [...ids];
  const present = new Set(ids);
  const before = (a: string, b: string): boolean => (thread ? compareIds(a, b) < 0 : compareIds(a, b) > 0);
  for (const id of added) {
    if (present.has(id)) continue;
    present.add(id);
    const at = result.findIndex((other) => before(id, other));
    if (at < 0) result.push(id);
    else result.splice(at, 0, id);
  }
  return result;
}

/**
 * What of a timeline's new list is shown at once, and what waits for "Show new beeps" (Twitter's "Show 12 posts"):
 * beeps newer than the one at its top that it did not have, unless `atOnce` (the viewer's own). Putting them in above
 * would push down what is being read. An empty list shows everything.
 */
export function holdNewer(ids: readonly string[], had: ReadonlySet<string>, top: string | undefined, atOnce: (id: string) => boolean): { ids: string[]; held: string[] } {
  if (top === undefined) return { ids: [...ids], held: [] };
  const shown: string[] = [];
  const held: string[] = [];
  for (const id of ids) {
    if (!had.has(id) && compareIds(id, top) > 0 && !atOnce(id)) held.push(id);
    else shown.push(id);
  }
  return { ids: shown, held };
}
