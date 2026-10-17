// Every beep the app shows, one signal each, so a beep shown twice (in a list and in an inbox entry) is one beep: a new
// count, a like from another tab or a redrawn card shows everywhere at once. `null` is a beep that went (deleted,
// hidden, its author removed, or no longer visible to the viewer); lists leave those out.
import { signal, type Signal } from '@preact/signals';
import { useLayoutEffect } from 'preact/hooks';
import { getJson } from './http.ts';
import type { BeepProps } from './props.ts';
import { schedule } from './schedule.ts';
import { mergeIds } from './live-url.ts';

const LIVE_BEEPS = '/live/beeps';

const beeps = new Map<string, Signal<BeepProps | null | undefined>>();
// How many cards show each beep now; the live connection follows these (live.ts).
const shown = new Map<string, number>();

/** Bumped when the beeps on the page change, so the live connection can follow them. */
export const shownChanged = signal(0);

export function beepSignal(id: string): Signal<BeepProps | null | undefined> {
  let entry = beeps.get(id);
  if (!entry) {
    entry = signal<BeepProps | null | undefined>(undefined);
    beeps.set(id, entry);
  }
  return entry;
}

export function putBeeps(list: readonly BeepProps[]): void {
  for (const beep of list) beepSignal(beep.id).value = beep;
}

export function removeBeep(id: string): void {
  const entry = beeps.get(id);
  if (entry) entry.value = null;
}

/** A banned or deleted account: everything by it goes, wherever it is shown. */
export function removeAuthors(ids: readonly string[]): void {
  const gone = new Set(ids);
  for (const entry of beeps.values()) {
    if (entry.value && gone.has(entry.value.author.id)) entry.value = null;
  }
}

export function setCounts(id: string, likeCount: number, replyCount: number): void {
  const entry = beeps.get(id);
  if (entry?.value) entry.value = { ...entry.value, likeCount, replyCount };
}

/**
 * Shows the beep as liked or not. The count moves only when the button actually changes, so a like made here and the
 * live event telling of the same like count once; the next counts event has the exact number anyway.
 */
export function setLiked(id: string, liked: boolean): void {
  const entry = beeps.get(id);
  const beep = entry?.value;
  if (!entry || !beep || !!beep.liked === liked) return;
  entry.value = { ...beep, liked, likeCount: Math.max(0, beep.likeCount + (liked ? 1 : -1)) };
}

/** Counts a card showing the beep while it is on the page. */
export function useShown(id: string): void {
  // Before the page is shown, so the live connection opened for it names this beep from the start.
  useLayoutEffect(() => {
    const count = shown.get(id) ?? 0;
    shown.set(id, count + 1);
    if (count === 0) shownChanged.value++;
    return () => {
      const left = (shown.get(id) ?? 1) - 1;
      if (left > 0) {
        shown.set(id, left);
      } else {
        shown.delete(id);
        shownChanged.value++;
      }
    };
  }, [id]);
}

/** The beeps on the page, in the order they came. */
export function shownIds(): string[] {
  return [...shown.keys()];
}

export function isShown(id: string): boolean {
  return shown.has(id);
}

/** Beeps on the page that show this actor: written by them, mentioning them, or answering one of theirs. */
export function shownWith(actorId: string): string[] {
  const theirs = new Set<string>();
  const found = new Set<string>();
  for (const id of shown.keys()) {
    const beep = beeps.get(id)?.value;
    if (!beep) continue;
    if (beep.author.id === actorId) theirs.add(id);
    if (beep.author.id === actorId || beep.mentions.includes(actorId)) found.add(id);
  }
  for (const id of shown.keys()) {
    const replyTo = beeps.get(id)?.value?.replyToId;
    if (replyTo && theirs.has(replyTo)) found.add(id);
  }
  return [...found];
}

// ---- Drawing beeps again -----------------------------------------------------------------------

const changed = new Set<string>();

/** Fetches these beeps again (they look different now), wherever they are shown; the ones the viewer may no longer see go. */
export function redraw(ids: readonly string[]): void {
  let any = false;
  for (const id of ids) {
    if (!shown.has(id)) continue;
    changed.add(id);
    any = true;
  }
  if (any) schedule(redrawChanged);
}

async function redrawChanged(): Promise<void> {
  const ids = mergeIds([...changed].filter((id) => shown.has(id)));
  ids.forEach((id) => changed.delete(id));
  if (ids.length === 0) return;
  let fresh: BeepProps[];
  try {
    fresh = (await getJson<{ items: BeepProps[] }>(`${LIVE_BEEPS}?${new URLSearchParams({ ids: ids.join(',') })}`)).items;
  } catch (error) {
    // Still to do when it is tried again.
    ids.forEach((id) => changed.add(id));
    throw error;
  }
  putBeeps(fresh);
  const found = new Set(fresh.map((beep) => beep.id));
  for (const id of ids) if (!found.has(id)) removeBeep(id);
  // More changed than one request carries: the rest go next.
  if (changed.size > 0) schedule(redrawChanged);
}

/** New beeps for a list: those after (or, for a newest-first list, also before) the given ones, for this viewer. */
export async function fetchNew(watch: string, after: string, before?: string): Promise<BeepProps[]> {
  const params = new URLSearchParams({ watch, after });
  if (before) params.set('before', before);
  const items = (await getJson<{ items: BeepProps[] }>(`${LIVE_BEEPS}?${params}`)).items;
  putBeeps(items);
  return items;
}
