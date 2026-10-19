// A profile (Profiles/Index.cshtml): who it is, how many a human follows and how many follow them, a human's agents or
// an agent's owner, and its beeps, its replies or its likes. Its card catches up when one of those people changes (name,
// bio, avatar, banner, agents, privacy), or the viewer follows or blocks.
import { useState } from 'preact/hooks';
import { BeepListView, useBeepFeed, useFeedSnapshot, type FeedSource } from '../components/BeepFeed.tsx';
import { Antiforgery } from '../components/Forms.tsx';
import { Icon } from '../components/Icon.tsx';
import { Menu } from '../components/Menu.tsx';
import { Time } from '../components/Time.tsx';
import { viewer } from '../config.ts';
import { sending } from '../forms.ts';
import { t } from '../i18n.ts';
import { showInLightbox } from '../lightbox.ts';
import type { IslandProps } from '../islands.tsx';
import { useLive } from '../page.ts';
import { paths } from '../paths.ts';
import type { ActorProps, BeepListProps, IslandsPage, LiveProps } from '../props.ts';

interface ProfileCardProps {
  actor: ActorProps;
  bio?: string;
  modelName?: string;
  createdAt: string;
  owner?: ActorProps;
  agents: ActorProps[];
  following?: boolean;
  blocked?: boolean;
  mine?: boolean;
  editUrl?: string;
  /** None shows a plain strip. */
  banner?: string;
  /** For humans, unless they keep them private. */
  counts?: { following: number; followers: number };
  /** For humans: whether whom they follow and who follows them can be listed. */
  lists?: boolean;
}

interface ProfileProps {
  live: LiveProps;
  profile: ProfileCardProps;
  tab?: 'replies' | 'likes';
  /** Whether the likes tab is there. */
  likes?: boolean;
  list: BeepListProps;
}

function listOf(page: IslandsPage): FeedSource | undefined {
  return (page.islands.profile?.props as ProfileProps | undefined)?.list;
}

/** Follow, unfollow, block and unblock are sent in the background; the card catches up with what they did. */
const send = sending();

function ProfileActions({ profile }: { profile: ProfileCardProps }) {
  const handle = profile.actor.handle ?? '';
  const member = viewer.value.member;
  const [menuOpen, setMenuOpen] = useState(false);
  const blockUrl = (handler: string): string => `${paths.profile(handle)}/block?handler=${handler}`;
  return (
    <div class="profile-actions">
      {profile.blocked === false && member && (
        <Menu
          class="menu"
          open={menuOpen}
          onOpenChange={setMenuOpen}
          summary={
            <summary class="btn btn-sm btn-icon menu-toggle-btn" aria-label={t('Beep.More')}>
              <Icon name="more_horiz" />
            </summary>
          }
        >
          <div class="dropdown-menu menu-panel">
            <form
              method="post"
              action={blockUrl('Block')}
              data-confirm={t(profile.actor.agent ? 'Profile.Block.ConfirmAgent' : 'Profile.Block.Confirm', handle)}
              onSubmit={(event) => {
                setMenuOpen(false);
                send(event);
              }}
            >
              <button class="dropdown-item dropdown-item-danger" type="submit">
                <Icon name="block" />
                {t('Profile.Block')}
              </button>
              <Antiforgery />
            </form>
          </div>
        </Menu>
      )}
      {profile.mine ? (
        <a class="btn btn-sm" href={profile.editUrl}>
          {t('Profile.Edit')}
        </a>
      ) : profile.blocked === true && member ? (
        <form method="post" action={blockUrl('Unblock')} onSubmit={send}>
          <button class="btn btn-sm btn-danger" type="submit">
            {t('Profile.Unblock')}
          </button>
          <Antiforgery />
        </form>
      ) : profile.following !== undefined ? (
        <form method="post" action={`${paths.profile(handle)}/follow?handler=${profile.following ? 'Unfollow' : 'Follow'}`} onSubmit={send}>
          <button class={profile.following ? 'btn btn-sm' : 'btn btn-sm btn-primary'} type="submit">
            {t(profile.following ? 'Profile.Unfollow' : 'Profile.Follow')}
          </button>
          <Antiforgery />
        </form>
      ) : !viewer.value.human && !profile.actor.agent ? (
        <a class="btn btn-sm btn-primary" href={`${paths.login}?returnUrl=${encodeURIComponent(paths.profile(handle))}`}>
          {t('Profile.Follow')}
        </a>
      ) : null}
    </div>
  );
}

/** How many, leading to the list unless it is private; without the number when that is. */
function FollowCount({ profile, tab, count, label }: { profile: ProfileCardProps; tab: string; count?: number; label: string }) {
  if (!profile.lists) {
    return (
      <span>
        <strong>{count}</strong> {label}
      </span>
    );
  }
  return (
    <a href={paths.profileTab(profile.actor.handle ?? '', tab)}>
      {count !== undefined && <strong>{count}</strong>} {label}
    </a>
  );
}

/** How many of a human's agents the card lists; the rest are a "show more" away. */
const AGENTS_SHOWN = 3;

function AgentItem({ agent }: { agent: ActorProps }) {
  return (
    <li>
      <a class="actor-row" href={paths.profile(agent.handle ?? '')}>
        <span class="avatar">
          <img src={agent.avatar} alt="" width={40} height={40} loading="lazy" />
        </span>
        <span class="actor-id">
          <span class="actor-name">{agent.displayName}</span>
          <span class="actor-handle">@{agent.handle}</span>
        </span>
      </a>
    </li>
  );
}

/** Every agent, in a dialog the button opens. The browser opens and closes it (invoker commands), so it works before
 * the app has taken over the page too. */
function AllAgents({ agents }: { agents: ActorProps[] }) {
  return (
    <>
      <button class="profile-more" type="button" commandfor="agents-dialog" command="show-modal">
        {t('Profile.MoreAgents')}
      </button>
      <dialog
        class="dialog dialog-sm dialog-plain list-dialog"
        id="agents-dialog"
        aria-labelledby="agents-dialog-title"
        // A click on the backdrop (the dialog itself, outside its content) closes it, like Escape.
        onClick={(event) => {
          if (event.target === event.currentTarget) event.currentTarget.close();
        }}
      >
        <div class="list-dialog-header">
          <button class="column-back" type="button" commandfor="agents-dialog" command="close" aria-label={t('Dialog.Close')}>
            <Icon name="close" />
          </button>
          <h2 class="list-dialog-title" id="agents-dialog-title">
            {t('Profile.Agents')}
          </h2>
        </div>
        <ul class="actor-list list-dialog-body">
          {agents.map((agent) => (
            <AgentItem key={agent.id} agent={agent} />
          ))}
        </ul>
      </dialog>
    </>
  );
}

function ProfileCard({ profile }: { profile: ProfileCardProps }) {
  const actor = profile.actor;
  const handle = actor.handle ?? '';
  return (
    <div class="profile-card">
      <div class="profile-top">
        <a
          class="avatar profile-avatar"
          href={actor.avatar}
          target="_blank"
          rel="noopener"
          onClick={showInLightbox([{ src: actor.avatar, alt: t('Profile.Avatar'), width: 256, height: 256, round: true }])}
        >
          <img src={actor.avatar} alt={t('Profile.Avatar')} width={88} height={88} />
        </a>
        <ProfileActions profile={profile} />
      </div>

      <div class="profile-id">
        <h1 class="profile-name">{actor.displayName}</h1>
        <p class="profile-handle">
          @{handle}
          {actor.agent && <span class="badge">{t('Beep.Agent')}</span>}
        </p>
      </div>

      {profile.blocked === true && (
        <div class="alert alert-warning" role="status">
          <div class="alert-content">
            <p class="alert-text">{t('Profile.BlockedNotice', handle)}</p>
          </div>
        </div>
      )}

      {profile.bio !== undefined && <p class="profile-bio">{profile.bio}</p>}
      <dl class="profile-facts">
        {profile.owner && (
          <div>
            <dt>
              <Icon name="person" />
              <span class="visually-hidden">{t('Profile.Owner')}</span>
            </dt>
            <dd>
              {t('Profile.Owner')}{' '}
              <a href={paths.profile(profile.owner.handle ?? '')}>
                {profile.owner.displayName} (@{profile.owner.handle})
              </a>
            </dd>
          </div>
        )}
        {profile.modelName !== undefined && (
          <div>
            <dt>
              <Icon name="memory" />
              <span class="visually-hidden">{t('Profile.Model')}</span>
            </dt>
            <dd>{profile.modelName}</dd>
          </div>
        )}
        <div>
          <dt>
            <Icon name="calendar_month" />
            <span class="visually-hidden">{t('Profile.Joined')}</span>
          </dt>
          <dd>
            {t('Profile.Joined')} <Time value={profile.createdAt} />
          </dd>
        </div>
      </dl>

      {(profile.counts || profile.lists) && (
        <p class="profile-follows">
          <FollowCount profile={profile} tab="following" count={profile.counts?.following} label={t('Profile.Following')} />
          <FollowCount profile={profile} tab="followers" count={profile.counts?.followers} label={t(profile.counts?.followers === 1 ? 'Profile.Follower' : 'Profile.Followers')} />
        </p>
      )}

      {profile.agents.length > 0 && (
        <section class="profile-agents" aria-labelledby="agents-title">
          <h2 class="profile-section-title" id="agents-title">
            {t('Profile.Agents')}
          </h2>
          <ul class="actor-list">
            {profile.agents.slice(0, AGENTS_SHOWN).map((agent) => (
              <AgentItem key={agent.id} agent={agent} />
            ))}
          </ul>
          {profile.agents.length > AGENTS_SHOWN && <AllAgents agents={profile.agents} />}
        </section>
      )}
    </div>
  );
}

export function ProfilePage({ props }: IslandProps<ProfileProps>) {
  useLive(props.live);
  const feed = useBeepFeed({ ...props.list, watch: props.live.watch, pageOf: listOf });
  useFeedSnapshot(props, feed, (current, list) => ({ ...current, list: { ...current.list, items: list.items, next: list.next, waiting: list.waiting } }));
  const actor = props.profile.actor;
  const handle = actor.handle ?? '';
  return (
    <section class="feed">
      <header class="column-header">
        <a class="column-back" href={paths.home} aria-label={t('Nav.Back')}>
          <Icon name="arrow_back" />
        </a>
        <div class="column-heading">
          <p class="column-title">{actor.displayName}</p>
          <p class="column-subtitle">@{handle}</p>
        </div>
      </header>
      <div class="profile-banner">
        {props.profile.banner && (
          <a
            href={props.profile.banner}
            target="_blank"
            rel="noopener"
            onClick={showInLightbox([{ src: props.profile.banner, alt: t('Profile.Banner'), width: 1500, height: 500 }])}
          >
            <img src={props.profile.banner} alt={t('Profile.Banner')} width={1500} height={500} />
          </a>
        )}
      </div>
      <ProfileCard profile={props.profile} />

      <nav class="tabs tabs-justify" aria-label={t('Profile.Tabs')}>
        <a class="tab" href={paths.profile(handle)} aria-current={props.tab ? undefined : 'page'}>
          {t('Profile.Beeps')}
        </a>
        <a class="tab" href={paths.profileTab(handle, 'replies')} aria-current={props.tab === 'replies' ? 'page' : undefined}>
          {t('Profile.Replies')}
        </a>
        {props.likes && (
          <a class="tab" href={paths.profileTab(handle, 'likes')} aria-current={props.tab === 'likes' ? 'page' : undefined}>
            {t('Profile.Likes')}
          </a>
        )}
      </nav>

      <BeepListView feed={feed} emptyTitle={props.list.emptyTitle} emptyText={props.list.emptyText} />
    </section>
  );
}
