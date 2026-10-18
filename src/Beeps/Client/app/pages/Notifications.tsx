// The inbox (Notifications.cshtml): mentions, replies and likes, newest first, drawn as Twitter draws them: a reply or a
// mention is its beep, and the likes of one beep are one entry from its newest liker, which a new like brings back to
// the top under a new id. Seeing a beep here leaves its notification
// unread, unlike elsewhere (seen.ts). Its first page follows the inbox live: new entries come in at the top, entries
// read in another tab or gone with their beep catch up. An older page is drawn again when the inbox changes.
import type { TargetedMouseEvent, TargetedSubmitEvent } from 'preact';
import { useRef } from 'preact/hooks';
import { signal, type Signal } from '@preact/signals';
import { beepSignal, putBeeps, useShown } from '../beeps.ts';
import { BeepCard } from '../components/BeepCard.tsx';
import { Antiforgery } from '../components/Forms.tsx';
import { Icon } from '../components/Icon.tsx';
import { Pager } from '../components/Pager.tsx';
import { sendForm } from '../forms.ts';
import { getIslands, getJson } from '../http.ts';
import { t } from '../i18n.ts';
import { useSnapshot, type IslandProps } from '../islands.tsx';
import { compareIds } from '../live-url.ts';
import { refreshPage, useLive, useLiveEvent } from '../page.ts';
import { paths } from '../paths.ts';
import type { ActorProps, LiveProps, NotificationProps, PageProps } from '../props.ts';
import { visit } from '../router.ts';
import { schedule } from '../schedule.ts';

interface NotificationsProps {
  live: LiveProps;
  page: PageProps<NotificationProps>;
}

const LIVE_NOTIFICATIONS = '/live/notifications';
// The inbox page's first page (PageRequest.DefaultLimit) and the most the server lists at once (MaxLimit).
const INBOX_PAGE = 20;
const INBOX_MAX = 100;

/** What an entry is about: one per inbox, so the same likes shown again under a newer id replace the older. */
function about(entry: NotificationProps): string {
  return `${entry.type}:${entry.beep.id}`;
}

interface InboxState {
  items: Signal<NotificationProps[]>;
  next: Signal<string | undefined>;
  source: PageProps<NotificationProps>;
  resync: () => Promise<void>;
}

function nameOf(actor: ActorProps): string {
  return actor.displayName ?? actor.handle ?? '';
}

/** Shown only while unread, at the entry's edge. */
function UnreadDot({ entry }: { entry: NotificationProps }) {
  if (entry.read) return null;
  return (
    <span class="notification-dot" title={t('Notifications.New')}>
      <span class="visually-hidden">{t('Notifications.New')}</span>
    </span>
  );
}

/**
 * Likes, as Twitter shows them: the icon in the avatar column, the faces of the newest who liked, who they are, and
 * the liked beep's text greyed. The whole entry opens the beep.
 */
function LikeEntry({ entry }: { entry: NotificationProps }) {
  useShown(entry.beep.id);
  const beep = beepSignal(entry.beep.id).value ?? entry.beep;
  const from = entry.from;
  const others = entry.others ?? 0;
  const permalink = paths.beep(entry.beep.id);
  const open = (event: TargetedMouseEvent<HTMLElement>): void => {
    if (event.defaultPrevented || event.button !== 0 || !(event.target instanceof Element)) return;
    if (event.target.closest('a, button') || window.getSelection()?.toString()) return;
    if (event.metaKey || event.ctrlKey) window.open(permalink, '_blank', 'noopener');
    else void visit(permalink).catch(() => {});
  };
  return (
    <li class={entry.read ? 'notification' : 'notification notification-unread'}>
      <UnreadDot entry={entry} />
      <article class="notification-group" onClick={open}>
        <span class="notification-icon notification-icon-like" aria-hidden="true">
          <Icon name="favorite" />
        </span>
        <div class="notification-main">
          <div class="notification-avatars">
            {[from, ...(entry.earlier ?? [])].map((actor) => (
              <a class="avatar" href={paths.profile(actor.handle ?? '')} title={nameOf(actor)} key={actor.id}>
                <img src={actor.avatar} alt="" width={32} height={32} loading="lazy" />
              </a>
            ))}
          </div>
          <p class="notification-text">
            <a class="notification-name" href={paths.profile(from.handle ?? '')}>
              {nameOf(from)}
            </a>{' '}
            {others > 0 && <>{others === 1 ? t('Notifications.OneOther') : t('Notifications.Others', others)} </>}
            <a class="notification-link" href={permalink}>
              {t('Notifications.Like')}
            </a>
          </p>
          {beep.html.length > 0 && <div class="notification-quote" dangerouslySetInnerHTML={{ __html: beep.html }} />}
        </div>
      </article>
    </li>
  );
}

/** A reply or a mention: the beep itself, as Twitter shows it. */
function BeepEntry({ entry }: { entry: NotificationProps }) {
  return (
    <li class={entry.read ? 'notification' : 'notification notification-unread'}>
      <UnreadDot entry={entry} />
      <p class="visually-hidden">
        {nameOf(entry.from)} {t(entry.type === 'reply' ? 'Notifications.Reply' : 'Notifications.Mention')}
      </p>
      <BeepCard id={entry.beep.id} inInbox />
    </li>
  );
}

function create(page: PageProps<NotificationProps>, live: boolean): InboxState {
  putBeeps(page.items.map((entry) => entry.beep));
  const state: InboxState = {
    items: signal(page.items),
    next: signal(page.next),
    source: page,
    // The first page as it is now: new entries, entries read in another tab, entries gone with their beep. A full
    // answer reaches back to its oldest entry: the entries shown past that (pushed down by new ones, or loaded as the
    // page was scrolled) stay below it.
    resync: async () => {
      if (!live) return;
      const shown = state.items.value;
      const limit = Math.min(Math.max(shown.length, INBOX_PAGE), INBOX_MAX);
      const fresh = (await getJson<{ items: NotificationProps[] }>(`${LIVE_NOTIFICATIONS}?limit=${limit}`)).items;
      putBeeps(fresh.map((entry) => entry.beep));
      const whole = fresh.length < limit;
      const oldest = fresh.at(-1)?.id ?? '';
      const current = new Set(fresh.map(about));
      const below = whole ? [] : shown.filter((entry) => compareIds(entry.id, oldest) < 0 && !current.has(about(entry)));
      state.items.value = [...fresh, ...below];
      if (whole) state.next.value = undefined;
    },
  };
  return state;
}

export function NotificationsPage({ props }: IslandProps<NotificationsProps>) {
  useLive(props.live);
  const live = props.live.watch === 'notifications';
  const ref = useRef<InboxState | null>(null);
  if (!ref.current) ref.current = create(props.page, live);
  const state = ref.current;
  // Fresh props (an older page drawn again, the inbox marked read): the list as the server has it now.
  if (props.page !== state.source) {
    state.source = props.page;
    putBeeps(props.page.items.map((entry) => entry.beep));
    state.items.value = props.page.items;
    state.next.value = props.page.next;
  }

  useLiveEvent('inbox', () => {
    if (live) schedule(state.resync);
    else refreshPage();
  });
  useLiveEvent('actor', (id: string) => {
    if (state.items.value.some((entry) => entry.from.id === id)) {
      if (live) schedule(state.resync);
      else refreshPage();
    }
  });
  useLiveEvent('resync', () => schedule(state.resync));
  useSnapshot(() => ({ ...props, page: { items: state.items.value, next: state.next.value } }));

  // An entry goes with its beep.
  const items = state.items.value.filter((entry) => beepSignal(entry.beep.id).value !== null);
  const unread = items.some((entry) => !entry.read);

  const loadMore = async (): Promise<boolean> => {
    const next = state.next.value;
    if (!next) return true;
    const answer = await getIslands(next);
    const page = (answer.page?.islands.notifications?.props as NotificationsProps | undefined)?.page;
    if (!page) return false;
    putBeeps(page.items.map((entry) => entry.beep));
    const shown = new Set(state.items.value.map(about));
    state.items.value = [...state.items.value, ...page.items.filter((entry) => !shown.has(about(entry)))];
    state.next.value = page.next;
    return true;
  };

  const markRead = (event: TargetedSubmitEvent<HTMLFormElement>): void => {
    event.preventDefault();
    void sendForm(event.currentTarget, event.submitter ?? null);
  };

  return (
    <section class="feed">
      <header class="column-header">
        <h1 class="column-title">{t('Notifications.Title')}</h1>
        {/* Marks up to the newest one shown, so ones that arrive meanwhile stay unread; there once one is unread. */}
        <form class="column-actions" method="post" action="/notifications?handler=Read" hidden={!unread} onSubmit={markRead}>
          <input type="hidden" name="upToId" value={items[0]?.id ?? ''} />
          <button class="btn btn-sm btn-ghost" type="submit">
            <Icon name="done_all" />
            {t('Notifications.MarkRead')}
          </button>
          <Antiforgery />
        </form>
      </header>
      <div class="empty" hidden={items.length > 0}>
        <h2 class="empty-title">{t('Notifications.Empty.Title')}</h2>
        <p class="empty-text">{t('Notifications.Empty.Text')}</p>
      </div>
      {items.length > 0 && (
        <ol class="notification-list">
          {items.map((entry) => (entry.type === 'like' ? <LikeEntry key={entry.id} entry={entry} /> : <BeepEntry key={entry.id} entry={entry} />))}
        </ol>
      )}
      {items.length > 0 && state.next.value && (
        <Pager key={state.next.value} href={state.next.value} label={t('Pager.More')} onMore={loadMore} />
      )}
    </section>
  );
}
