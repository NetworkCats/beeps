// Searching beeps and people (Search.cshtml). The results are asked again as the search index changes (new beeps,
// renamed people), at most every few seconds, and load their next pages as they are scrolled.
import { useRef } from 'preact/hooks';
import { signal, type Signal } from '@preact/signals';
import { beepSignal, putBeeps } from '../beeps.ts';
import { BeepCard } from '../components/BeepCard.tsx';
import { FormErrors } from '../components/Forms.tsx';
import { Icon } from '../components/Icon.tsx';
import { Pager } from '../components/Pager.tsx';
import { PersonRow, type Person } from '../components/PersonRow.tsx';
import { getIslands } from '../http.ts';
import { t } from '../i18n.ts';
import { useSnapshot, type IslandProps } from '../islands.tsx';
import { appendPage, mergeFresh } from '../lists.ts';
import { useLive } from '../page.ts';
import type { BeepProps, LiveProps } from '../props.ts';

interface SearchResults {
  beeps: BeepProps[];
  users: Person[];
  next?: string;
}

interface SearchProps {
  live: LiveProps;
  query?: string;
  users?: boolean;
  results?: SearchResults;
  beepsUrl: string;
  usersUrl: string;
}

interface ResultsState {
  ids: Signal<string[]>;
  users: Signal<Person[]>;
  next: Signal<string | undefined>;
  source?: SearchResults;
}

/** The results as they are drawn: fresh ones (the index changed) are taken in without losing the pages loaded past them. */
function useResults(results: SearchResults | undefined, people: boolean): ResultsState {
  const ref = useRef<ResultsState | null>(null);
  if (!ref.current) {
    putBeeps(results?.beeps ?? []);
    ref.current = { ids: signal((results?.beeps ?? []).map((beep) => beep.id)), users: signal(results?.users ?? []), next: signal(results?.next), source: results };
  }
  const state = ref.current;
  if (results !== state.source) {
    state.source = results;
    putBeeps(results?.beeps ?? []);
    const beeps = mergeFresh({ ids: state.ids.value, next: state.next.value }, { ids: (results?.beeps ?? []).map((beep) => beep.id), next: results?.next });
    const userIds = state.users.value.map((user) => user.actor.id);
    const users = mergeFresh({ ids: userIds, next: state.next.value }, { ids: (results?.users ?? []).map((user) => user.actor.id), next: results?.next });
    const byId = new Map([...state.users.value, ...(results?.users ?? [])].map((user) => [user.actor.id, user]));
    state.ids.value = beeps.ids;
    state.users.value = users.ids.flatMap((id) => byId.get(id) ?? []);
    state.next.value = people ? users.next : beeps.next;
  }
  return state;
}

export function SearchPage({ props, errors }: IslandProps<SearchProps>) {
  useLive(props.live);
  const state = useResults(props.results, !!props.users);
  const results = props.results;
  const beeps = state.ids.value.filter((id) => beepSignal(id).value);
  const users = state.users.value;
  useSnapshot(() => ({
    ...props,
    results: results && { beeps: beeps.map((id) => beepSignal(id).value as BeepProps), users, next: state.next.value },
  }));

  const loadMore = async (): Promise<boolean> => {
    const next = state.next.value;
    if (!next) return true;
    const answer = await getIslands(next);
    const page = (answer.page?.islands.search?.props as SearchProps | undefined)?.results;
    if (!page) return false;
    putBeeps(page.beeps);
    const shownUsers = new Set(state.users.value.map((user) => user.actor.id));
    state.ids.value = appendPage({ ids: state.ids.value }, { ids: page.beeps.map((beep) => beep.id) }).ids;
    state.users.value = [...state.users.value, ...page.users.filter((user) => !shownUsers.has(user.actor.id))];
    state.next.value = page.next;
    return true;
  };

  const empty = props.users ? users.length === 0 : beeps.length === 0;
  const next = state.next.value;
  return (
    <section class="feed search-page">
      <header class="column-header column-header-search">
        <h1 class="visually-hidden">{t('Search.Title')}</h1>
        <form class="search search-pill" role="search" method="get" action="/search">
          <input type="hidden" name="type" value={props.users ? 'users' : 'beeps'} />
          <Icon name="search" />
          <label class="visually-hidden" for="q">
            {t('Search.Label')}
          </label>
          <input class="search-input" type="search" id="q" name="q" defaultValue={props.query} placeholder={t('Search.Placeholder')} autocomplete="off" required />
        </form>
      </header>
      <FormErrors errors={errors} />
      {!results && (
        <div class="empty search-hint">
          <span class="empty-icon empty-icon-circle">
            <Icon name="search" />
          </span>
          <p class="empty-text">{t('Search.Hint')}</p>
        </div>
      )}

      {results && (
        <nav class="tabs tabs-justify search-tabs" aria-label={t('Search.Tabs')}>
          <a class="tab" href={props.beepsUrl} aria-current={props.users ? undefined : 'page'}>
            {t('Search.Beeps')}
          </a>
          <a class="tab" href={props.usersUrl} aria-current={props.users ? 'page' : undefined}>
            {t('Search.Users')}
          </a>
        </nav>
      )}
      {results && empty && (
        <div class="empty">
          <h2 class="empty-title">{t('Search.Empty.Title')}</h2>
          <p class="empty-text">{t('Search.Empty.Text')}</p>
        </div>
      )}
      {results && !empty && props.users && (
        <ul class="actor-list people-list">
          {users.map((result) => (
            <PersonRow key={result.actor.id} person={result} />
          ))}
        </ul>
      )}
      {results && !empty && !props.users && (
        <div class="beep-list">
          {beeps.map((id) => (
            <BeepCard key={id} id={id} />
          ))}
        </div>
      )}
      {results && next && <Pager key={next} href={next} label={t('Pager.More')} onMore={loadMore} />}
    </section>
  );
}
