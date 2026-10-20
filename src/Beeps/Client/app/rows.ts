// The rows of a list that loads its next pages as it is scrolled (an admin table, whom someone follows). When the page's
// props come again (the site changed, an action was answered) the first page is taken in as it is now; once more
// pages were loaded, those past it stay, so the list does not shrink back under the reader's eyes, unless the first page
// is now the whole list.
import { useRef } from 'preact/hooks';
import { signal, type Signal } from '@preact/signals';
import { getIslands } from './http.ts';
import { useSnapshot } from './islands.tsx';
import { appendPage, mergeFresh } from './lists.ts';
import type { PageProps } from './props.ts';

interface RowsState<T> {
  rows: Signal<T[]>;
  next: Signal<string | undefined>;
  source: PageProps<T>;
  more: boolean;
}

interface Rows<T> {
  rows: T[];
  next?: string;
  /** Loads the next page into the list, for the Pager. */
  loadMore: () => Promise<boolean>;
}

interface RowsOptions<P, T> {
  /** The island, whose props the next page answers with. */
  island: string;
  /** Each row's key. */
  key: (row: T) => string;
  /** The list's page in the island's props. */
  pick: (props: P) => PageProps<T>;
  /** The island's props with the list as it is drawn now, for going back to the page. */
  snapshot: (page: PageProps<T>) => P;
}

export function useRows<P, T>(props: P, { island, key, pick, snapshot }: RowsOptions<P, T>): Rows<T> {
  const page = pick(props);
  const ref = useRef<RowsState<T> | null>(null);
  if (!ref.current) ref.current = { rows: signal(page.items), next: signal(page.next), source: page, more: false };
  const state = ref.current;
  if (page !== state.source) {
    state.source = page;
    if (state.more && page.next) {
      const byKey = new Map([...state.rows.value, ...page.items].map((row) => [key(row), row]));
      const merged = mergeFresh({ ids: state.rows.value.map(key), next: state.next.value }, { ids: page.items.map(key), next: page.next });
      state.rows.value = merged.ids.flatMap((id) => byKey.get(id) ?? []);
      state.next.value = merged.next;
    } else {
      state.rows.value = page.items;
      state.next.value = page.next;
    }
  }
  useSnapshot(() => snapshot({ items: state.rows.value, next: state.next.value }));

  const loadMore = async (): Promise<boolean> => {
    const next = state.next.value;
    if (!next) return true;
    const answer = await getIslands(next);
    const props = answer.page?.islands[island]?.props as P | undefined;
    if (!props) return false;
    const more = pick(props);
    const byKey = new Map([...more.items, ...state.rows.value].map((row) => [key(row), row]));
    const appended = appendPage({ ids: state.rows.value.map(key) }, { ids: more.items.map(key), next: more.next });
    state.rows.value = appended.ids.flatMap((id) => byKey.get(id) ?? []);
    state.next.value = appended.next;
    state.more = true;
    return true;
  };

  return { rows: state.rows.value, next: state.next.value, loadMore };
}
