// A list of beeps that follows the site live: a timeline (newest first) or a thread (oldest first). New beeps for its
// topic come in as they are posted, beeps that go leave it, a fresh copy of its first page (the page's props fetched
// again) is taken in without losing what was loaded past it, and its next pages load as it is scrolled. As on Twitter,
// a timeline does not put new beeps in above what is being read: they wait behind "Show 3 new beeps", at the top of
// the list or, scrolled away from it, in a pill under the header. The viewer's own come in at once, and a thread's
// replies, which come in below.
import { signal, type Signal } from '@preact/signals';
import { useEffect, useRef, useState } from 'preact/hooks';
import { beepSignal, fetchNew, putBeeps } from '../beeps.ts';
import { viewer } from '../config.ts';
import { getIslands } from '../http.ts';
import { t } from '../i18n.ts';
import { useSnapshot } from '../islands.tsx';
import { compareIds, NO_ID, previousId } from '../live-url.ts';
import { appendPage, holdNewer, insertOrdered, mergeFresh } from '../lists.ts';
import type { ActorProps } from '../props.ts';
import { Icon } from './Icon.tsx';
import { refreshPage, useLiveEvent } from '../page.ts';
import type { BeepProps, IslandsPage } from '../props.ts';
import { schedule } from '../schedule.ts';
import { BeepCard } from './BeepCard.tsx';
import { Pager } from './Pager.tsx';

// Matches LiveFeedService.Limit: how many beeps the server sends at most for one fetch.
const FETCH_LIMIT = 50;
// A burst bigger than this many answers is left for a resync.
const MAX_FETCH_ROUNDS = 20;

export interface FeedSource {
  items: BeepProps[];
  next?: string;
  /** New beeps that were held above the timeline when it was left, for when it is gone back to. */
  waiting?: BeepProps[];
}

export interface FeedOptions extends FeedSource {
  /** The live topic, when new beeps are added as they come (the first page of a timeline, a thread's last page). */
  watch?: string;
  thread?: boolean;
  /** For a thread: the cursor its page starts after (`?after=`), which a resync starts from too. */
  after?: string;
  /** The list in the answer for its next page; with the topic a thread's last page follows. */
  pageOf: (page: IslandsPage) => (FeedSource & { watch?: string }) | undefined;
}

export interface Feed {
  /** The beeps shown, in order, without those that went. */
  visible: string[];
  next?: string;
  loadMore: () => Promise<boolean>;
  /** The list as it is now, as props to draw it again from. */
  snapshot: () => FeedSource;
  /** Whether the beep came in while the list was on screen, and has yet to play its arrival (motion.ts). */
  entering: (id: string) => boolean;
  /** It has played it. */
  entered: (id: string) => void;
  /** New beeps waiting above a timeline, newest first. */
  waiting: string[];
  /** Puts them in. */
  showWaiting: () => void;
}

interface FeedState {
  ids: Signal<string[]>;
  /** Beeps that came in after the list was first drawn, other than its next pages: new ones, and ones that came back. */
  entering: Set<string>;
  /** New beeps held above a timeline (holdNewer), newest first. */
  waiting: Signal<string[]>;
  next: Signal<string | undefined>;
  watch: Signal<string | undefined>;
  source: FeedSource;
  options: FeedOptions;
  fetchNew: () => Promise<void>;
  resync: () => Promise<void>;
}

function visibleOf(ids: readonly string[]): string[] {
  return ids.filter((id) => beepSignal(id).value);
}

function mine(id: string): boolean {
  return beepSignal(id).value?.author.id === viewer.value.id;
}

/**
 * The list's new ids; those it did not have before come in, or, above a live timeline's top, wait to be shown. Those
 * waiting already keep waiting (the viewer's own beep, put in at once, may have moved the top past them). A list that
 * does not follow new beeps (an older page, the likes, in the order they were liked) holds none.
 */
function arrive(state: FeedState, ids: string[]): void {
  const had = new Set(state.ids.value);
  const top = state.options.thread || !state.watch.value ? undefined : visibleOf(state.ids.value)[0];
  const waiting = new Set(top === undefined ? [] : state.waiting.value);
  const { ids: shown, held } = holdNewer(ids.filter((id) => !waiting.has(id)), had, top, mine);
  for (const id of shown) if (!had.has(id)) state.entering.add(id);
  state.ids.value = shown;
  const shownIds = new Set(shown);
  if (held.length > 0 || state.waiting.value.some((id) => shownIds.has(id))) {
    state.waiting.value = insertOrdered(state.waiting.value.filter((id) => !shownIds.has(id)), held, false);
  }
}

function create(options: FeedOptions): FeedState {
  putBeeps(options.items);
  putBeeps(options.waiting ?? []);
  const state: FeedState = {
    ids: signal(options.items.map((beep) => beep.id)),
    entering: new Set(),
    waiting: signal((options.waiting ?? []).map((beep) => beep.id)),
    next: signal(options.next),
    watch: signal(options.watch),
    source: options,
    options,
    fetchNew: async () => {
      const watch = state.watch.value;
      if (!watch) return;
      const thread = !!state.options.thread;
      const shown = visibleOf(state.ids.value);
      if (thread) {
        // A thread grows at the bottom, oldest first: ask after the last one until all of it came.
        let after = shown.at(-1) ?? NO_ID;
        for (let round = 0; round < MAX_FETCH_ROUNDS; round++) {
          const fresh = await fetchNew(watch, after);
          arrive(state, insertOrdered(state.ids.value, fresh.map((beep) => beep.id), true));
          if (fresh.length < FETCH_LIMIT) break;
          after = fresh.at(-1)?.id ?? after;
        }
        return;
      }
      // A timeline grows at the top, newest first. An answer holds the newest ones; a full answer may leave some out
      // between them and what the page showed, so the rest are asked for below the oldest that came. Those waiting to
      // be shown came already.
      const waiting = visibleOf(state.waiting.value)[0];
      const after = waiting && (!shown[0] || compareIds(waiting, shown[0]) > 0) ? waiting : shown[0] ?? NO_ID;
      let before: string | undefined;
      for (let round = 0; round < MAX_FETCH_ROUNDS; round++) {
        const fresh = await fetchNew(watch, after, before);
        arrive(state, insertOrdered(state.ids.value, fresh.map((beep) => beep.id), false));
        const oldest = fresh.at(-1)?.id;
        if (fresh.length < FETCH_LIMIT || !oldest) break;
        before = oldest;
      }
    },
    // Brings the list up to date with what a reload would show, as far as one fetch reaches: beeps that came back are
    // put in their place, ones the viewer no longer sees go, and the rest are drawn again.
    resync: async () => {
      const watch = state.watch.value;
      if (!watch) return;
      const thread = !!state.options.thread;
      const shown = visibleOf(state.ids.value);
      const oldest = shown.at(-1);
      const after = thread ? state.options.after ?? NO_ID : oldest ? previousId(oldest) : NO_ID;
      const fresh = await fetchNew(watch, after);
      const freshIds = new Set(fresh.map((beep) => beep.id));
      // A full answer stops somewhere: past that point the list is left as it is.
      const edge = fresh.length >= FETCH_LIMIT ? fresh.at(-1)?.id : undefined;
      const inRange = (id: string): boolean =>
        compareIds(id, after) > 0 && (!edge || (thread ? compareIds(id, edge) <= 0 : compareIds(id, edge) >= 0));
      const kept = state.ids.value.filter((id) => !inRange(id) || freshIds.has(id));
      state.waiting.value = state.waiting.value.filter((id) => !inRange(id) || freshIds.has(id));
      arrive(state, insertOrdered(kept, fresh.map((beep) => beep.id), thread));
    },
  };
  return state;
}

/** The list's state, kept across the island's renders; new props (a fresh first page) are taken in as they come. */
export function useBeepFeed(options: FeedOptions): Feed {
  const ref = useRef<FeedState | null>(null);
  if (!ref.current) ref.current = create(options);
  const state = ref.current;
  state.options = options;
  if (options.items !== state.source.items) {
    putBeeps(options.items);
    const merged = mergeFresh({ ids: state.ids.value, next: state.next.value }, { ids: options.items.map((beep) => beep.id), next: options.next });
    state.source = options;
    arrive(state, merged.ids);
    state.next.value = merged.next;
    if (options.watch) state.watch.value = options.watch;
  }

  const visible = visibleOf(state.ids.value);

  useLiveEvent('beep', () => {
    if (state.watch.value) schedule(state.fetchNew);
  });
  useLiveEvent('resync', () => {
    if (state.watch.value) schedule(state.resync);
  });
  // A beep of the list went. A live list that has none left is filled from what the topic still has; an older page
  // is drawn again, as a reload would show the next one in its place.
  const gone = (): void => {
    if (visibleOf(state.ids.value).length === visible.length) return;
    if (!state.watch.value) refreshPage();
    else if (visibleOf(state.ids.value).length === 0) schedule(state.resync);
  };
  useLiveEvent('removed', gone);
  useLiveEvent('authors_removed', gone);

  const loadMore = async (): Promise<boolean> => {
    const next = state.next.value;
    if (!next) return true;
    const answer = await getIslands(next);
    const page = answer.page ? options.pageOf(answer.page) : undefined;
    if (!page) return false;
    putBeeps(page.items);
    const added = appendPage({ ids: state.ids.value, next }, { ids: page.items.map((beep) => beep.id), next: page.next });
    state.ids.value = added.ids;
    state.next.value = added.next;
    // A thread's last page follows its new replies.
    if (page.watch && !state.watch.value) state.watch.value = page.watch;
    return true;
  };

  const snapshot = (): FeedSource => ({
    items: visibleOf(state.ids.value).map((id) => beepSignal(id).value as BeepProps),
    next: state.next.value,
    waiting: visibleOf(state.waiting.value).map((id) => beepSignal(id).value as BeepProps),
  });

  const entering = (id: string): boolean => state.entering.has(id);
  const entered = (id: string): void => {
    state.entering.delete(id);
  };

  // Shown where the button was: nothing moves in, so nothing plays an arrival.
  const showWaiting = (): void => {
    state.ids.value = insertOrdered(state.ids.value, visibleOf(state.waiting.value), false);
    state.waiting.value = [];
  };

  return { visible, next: state.next.value, loadMore, snapshot, entering, entered, waiting: visibleOf(state.waiting.value), showWaiting };
}

/** Up to three of the waiting beeps' authors, newest first, for the pill. */
function authorsOf(ids: readonly string[]): ActorProps[] {
  const authors = new Map<string, ActorProps>();
  for (const id of ids) {
    const author = beepSignal(id).value?.author;
    if (author && !authors.has(author.id)) authors.set(author.id, author);
    if (authors.size === 3) break;
  }
  return [...authors.values()];
}

/**
 * "Show 3 new beeps": a row at the top of the list, and, once that is scrolled out of sight, a pill under the header
 * that goes back up to it. Either shows them.
 */
function WaitingBeeps({ feed }: { feed: Feed }) {
  const row = useRef<HTMLButtonElement>(null);
  const [away, setAway] = useState(false);
  const count = feed.waiting.length;
  useEffect(() => {
    const element = row.current;
    if (!element) {
      setAway(false);
      return undefined;
    }
    // Under the sticky header counts as out of sight.
    const header = document.querySelector('.column-header')?.getBoundingClientRect().height ?? 0;
    const observer = new IntersectionObserver(([entry]) => setAway(!entry?.isIntersecting), { rootMargin: `-${Math.round(header)}px 0px 0px 0px` });
    observer.observe(element);
    return () => observer.disconnect();
  }, [count > 0]);
  if (count === 0) return null;
  const label = count === 1 ? t('Feed.ShowNew.One') : t('Feed.ShowNew', count);
  const backUp = (): void => {
    row.current?.scrollIntoView({ block: 'start', behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
    feed.showWaiting();
  };
  return (
    <>
      {away && (
        <div class="feed-new-float">
          <button class="feed-new-pill" type="button" onClick={backUp}>
            <Icon name="arrow_upward" />
            <span class="feed-new-avatars" aria-hidden="true">
              {authorsOf(feed.waiting).map((author) => (
                <img key={author.id} src={author.avatar} alt="" width={24} height={24} />
              ))}
            </span>
            <span>{label}</span>
          </button>
        </div>
      )}
      <button ref={row} class="feed-new" type="button" onClick={feed.showWaiting}>
        {label}
      </button>
    </>
  );
}

export interface BeepListViewProps {
  feed: Feed;
  emptyTitle: string;
  emptyText?: string;
}

/** The list as Pages/Shared/_BeepList.cshtml draws it: the empty state (hidden while there are beeps), the beeps, the pager. */
export function BeepListView({ feed, emptyTitle, emptyText }: BeepListViewProps) {
  const { visible } = feed;
  return (
    <>
      <WaitingBeeps feed={feed} />
      <div class="empty" hidden={visible.length > 0}>
        <h2 class="empty-title">{emptyTitle}</h2>
        {emptyText && <p class="empty-text">{emptyText}</p>}
      </div>
      {visible.length > 0 && (
        <div>
          {visible.map((id) => (
            <BeepCard key={id} id={id} entering={feed.entering(id)} onEntered={() => feed.entered(id)} />
          ))}
        </div>
      )}
      {visible.length > 0 && feed.next && <Pager key={feed.next} href={feed.next} label={t('Pager.More')} onMore={feed.loadMore} />}
    </>
  );
}

/** For a page island: what to draw it from when it is gone back to, with its list as it was left. */
export function useFeedSnapshot<P>(props: P, feed: Feed, put: (props: P, list: FeedSource) => P): void {
  useSnapshot(() => put(props, feed.snapshot()));
}
