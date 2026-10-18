// A beep in its thread (Beep/Details.cshtml): the whole conversation from its first beep, oldest first, with this one
// marked and the reply form under it. The thread's last page takes in new replies as they are posted. Signed in, the
// page tells the site the beep was viewed, for the person's history and the beep's view count (shown to no one).
import { useEffect, useMemo } from 'preact/hooks';
import { beepSignal, putBeeps } from '../beeps.ts';
import { useBeepFeed, useFeedSnapshot, type FeedSource } from '../components/BeepFeed.tsx';
import { BeepCard } from '../components/BeepCard.tsx';
import { Composer } from '../components/Composer.tsx';
import { Icon } from '../components/Icon.tsx';
import { Pager } from '../components/Pager.tsx';
import { viewer } from '../config.ts';
import { formData } from '../http.ts';
import { t } from '../i18n.ts';
import type { IslandProps } from '../islands.tsx';
import { useLive } from '../page.ts';
import { paths } from '../paths.ts';
import type { BeepProps, ComposerProps, Errors, IslandsPage, LiveProps } from '../props.ts';

interface ThreadProps {
  live: LiveProps;
  focus: BeepProps;
  items: BeepProps[];
  next?: string;
  after?: string;
  reply?: ComposerProps;
}

function threadOf(page: IslandsPage): (FeedSource & { watch?: string }) | undefined {
  const props = page.islands.thread?.props as ThreadProps | undefined;
  return props && { items: props.items, next: props.next, watch: props.live.watch };
}

/**
 * Once the page is shown, not when it is fetched ahead (a link hovered). The server counts it once however often the
 * page is drawn again soon after (gone back to, shown anew).
 */
function recordView(id: string): void {
  if (!viewer.value.human) return;
  fetch(paths.beepView(id), { method: 'POST', body: formData(), credentials: 'same-origin', keepalive: true }).catch(() => {});
}

/** Under the beep the page is about: the reply form for members, a way in for everyone else (_ReplyBox.cshtml). */
function ReplyBox({ focus, reply, errors }: { focus: string; reply?: ComposerProps; errors: Errors }) {
  if (reply) {
    return (
      <div class="reply-box">
        <Composer composer={reply} errors={errors} />
      </div>
    );
  }
  return (
    <p class="reply-box reply-signin" id="reply">
      <a class="btn btn-sm" href={`${paths.login}?returnUrl=${encodeURIComponent(paths.beep(focus))}`}>
        {t('Beep.SignInToReply')}
      </a>
    </p>
  );
}

export function ThreadPage({ props, errors }: IslandProps<ThreadProps>) {
  useLive(props.live);
  const feed = useBeepFeed({ items: props.items, next: props.next, watch: props.live.watch, thread: true, after: props.after, pageOf: threadOf });
  useFeedSnapshot(props, feed, (current, list) => ({ ...current, items: list.items, next: list.next }));
  const focusId = props.focus.id;
  useEffect(() => recordView(focusId), [focusId]);
  // The beep the page is about, as the server last drew it, then as it changes.
  useMemo(() => putBeeps([props.focus]), [props.focus]);
  const focusShown = feed.visible.includes(focusId);
  // Who each beep on the page is by, so a reply can say whom it answers.
  const authors = new Map(feed.visible.map((id) => [id, beepSignal(id).value?.author.handle ?? null]));
  return (
    <section class="feed thread">
      <header class="column-header">
        <a class="column-back" href="/" aria-label={t('Nav.Back')}>
          <Icon name="arrow_back" />
        </a>
        <h1 class="column-title">{t('Thread.Title')}</h1>
      </header>
      {!focusShown && <BeepCard id={focusId} focused />}
      {!focusShown && <ReplyBox focus={focusId} reply={props.reply} errors={errors} />}
      <div class="beep-list">
        {feed.visible.map((id) => {
          const replyTo = beepSignal(id).value?.replyToId;
          const card = (
            <BeepCard
              key={id}
              id={id}
              focused={id === focusId}
              inThread
              replyingTo={replyTo ? authors.get(replyTo) : null}
              entering={feed.entering(id)}
              onEntered={() => feed.entered(id)}
            />
          );
          return id === focusId ? [card, <ReplyBox key="reply" focus={focusId} reply={props.reply} errors={errors} />] : card;
        })}
      </div>
      {feed.next && <Pager key={feed.next} href={feed.next} label={t('Pager.MoreReplies')} onMore={feed.loadMore} />}
    </section>
  );
}
