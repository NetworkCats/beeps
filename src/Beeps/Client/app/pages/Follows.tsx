// Whom a human follows, or who follows them (Profiles/Follows.cshtml), most recently followed first, under the two tabs
// the profile's counts lead to. Asked again when the human or someone listed changes (their privacy, a new name), or the
// viewer follows or unfollows someone; the next pages load as the list is scrolled.
import { Icon } from '../components/Icon.tsx';
import { Pager } from '../components/Pager.tsx';
import { PersonRow, type Person } from '../components/PersonRow.tsx';
import { t } from '../i18n.ts';
import type { IslandProps } from '../islands.tsx';
import { useLive } from '../page.ts';
import { paths } from '../paths.ts';
import type { ActorProps, LiveProps, PageProps } from '../props.ts';
import { useRows } from '../rows.ts';

interface FollowsProps {
  live: LiveProps;
  actor: ActorProps;
  /** Who follows them, rather than whom they follow. */
  followers?: boolean;
  /** They keep the lists to themselves. */
  hidden?: boolean;
  page: PageProps<Person>;
}

export function FollowsPage({ props }: IslandProps<FollowsProps>) {
  useLive(props.live);
  const { rows: people, next, loadMore } = useRows(props, {
    island: 'follows',
    key: (person: Person) => person.actor.id,
    pick: (p) => p.page,
    snapshot: (page) => ({ ...props, page }),
  });

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
