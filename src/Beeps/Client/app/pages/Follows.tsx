// Whom a human follows, or who follows them (Profiles/Follows.cshtml), most recently followed first, under the two tabs
// the profile's counts lead to. Asked again when the human or someone listed changes (their privacy, a new name), or the
// viewer follows or unfollows someone; the next pages load as the list is scrolled.
import { useRef } from 'preact/hooks';
import { signal, type Signal } from '@preact/signals';
import { Icon } from '../components/Icon.tsx';
import { Pager } from '../components/Pager.tsx';
import { PersonRow, type Person } from '../components/PersonRow.tsx';
import { getIslands } from '../http.ts';
import { t } from '../i18n.ts';
import { useSnapshot, type IslandProps } from '../islands.tsx';
import { appendPage, mergeFresh } from '../lists.ts';
import { useLive } from '../page.ts';
import { paths } from '../paths.ts';
import type { ActorProps, LiveProps } from '../props.ts';

interface PeoplePage {
  items: Person[];
  next?: string;
}

interface FollowsProps {
  live: LiveProps;
  actor: ActorProps;
  /** Who follows them, rather than whom they follow. */
  followers?: boolean;
  /** They keep the lists to themselves. */
  hidden?: boolean;
  page: PeoplePage;
}

interface PeopleState {
  people: Signal<Person[]>;
  next: Signal<string | undefined>;
  source: PeoplePage;
}

const idsOf = (people: Person[]): string[] => people.map((person) => person.actor.id);

/** The list as it is drawn: a fresh first page is taken in without losing the pages loaded past it. */
function usePeople(page: PeoplePage): PeopleState {
  const ref = useRef<PeopleState | null>(null);
  if (!ref.current) ref.current = { people: signal(page.items), next: signal(page.next), source: page };
  const state = ref.current;
  if (page !== state.source) {
    state.source = page;
    const merged = mergeFresh({ ids: idsOf(state.people.value), next: state.next.value }, { ids: idsOf(page.items), next: page.next });
    const byId = new Map([...state.people.value, ...page.items].map((person) => [person.actor.id, person]));
    state.people.value = merged.ids.flatMap((id) => byId.get(id) ?? []);
    state.next.value = merged.next;
  }
  return state;
}

export function FollowsPage({ props }: IslandProps<FollowsProps>) {
  useLive(props.live);
  const state = usePeople(props.page);
  const people = state.people.value;
  const next = state.next.value;
  useSnapshot(() => ({ ...props, page: { items: state.people.value, next: state.next.value } }));

  const loadMore = async (): Promise<boolean> => {
    const from = state.next.value;
    if (!from) return true;
    const answer = await getIslands(from);
    const page = (answer.page?.islands.follows?.props as FollowsProps | undefined)?.page;
    if (!page) return false;
    const added = appendPage({ ids: idsOf(state.people.value) }, { ids: idsOf(page.items), next: page.next });
    const byId = new Map([...state.people.value, ...page.items].map((person) => [person.actor.id, person]));
    state.people.value = added.ids.flatMap((id) => byId.get(id) ?? []);
    state.next.value = added.next;
    return true;
  };

  const actor = props.actor;
  const handle = actor.handle ?? '';
  const empty = props.hidden ? 'Follows.Hidden' : props.followers ? 'Follows.NoFollowers' : 'Follows.NoFollowing';
  return (
    <section class="feed">
      <header class="column-header">
        <a class="column-back" href={paths.profile(handle)} aria-label={t('Nav.Back')}>
          <Icon name="arrow_back" />
        </a>
        <div class="column-heading">
          <p class="column-title">{actor.displayName}</p>
          <p class="column-subtitle">@{handle}</p>
        </div>
      </header>
      <nav class="tabs tabs-justify" aria-label={t('Follows.Tabs')}>
        <a class="tab" href={paths.profileTab(handle, 'following')} aria-current={props.followers ? undefined : 'page'}>
          {t('Profile.Following')}
        </a>
        <a class="tab" href={paths.profileTab(handle, 'followers')} aria-current={props.followers ? 'page' : undefined}>
          {t('Profile.Followers')}
        </a>
      </nav>
      {people.length === 0 ? (
        <div class="empty">
          <h2 class="empty-title">{t(empty)}</h2>
        </div>
      ) : (
        <ul class="actor-list people-list">
          {people.map((person) => (
            <PersonRow key={person.actor.id} person={person} />
          ))}
        </ul>
      )}
      {people.length > 0 && next && <Pager key={next} href={next} label={t('Pager.More')} onMore={loadMore} />}
    </section>
  );
}
