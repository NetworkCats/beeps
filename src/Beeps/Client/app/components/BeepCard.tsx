// One beep, drawn exactly as Pages/Shared/_Beep.cshtml draws it, from the beep's signal (beeps.ts): a new count, a
// like from another tab or a redrawn beep shows at once, and a beep that went disappears.
import type { TargetedMouseEvent, TargetedSubmitEvent } from 'preact';
import { useState } from 'preact/hooks';
import { beepSignal, removeBeep, setLiked, useShown } from '../beeps.ts';
import { viewer } from '../config.ts';
import { confirmAction } from '../dialogs.ts';
import { formData } from '../http.ts';
import { t } from '../i18n.ts';
import { pageLive } from '../page.ts';
import { here, paths } from '../paths.ts';
import { visit } from '../router.ts';
import { useSeen } from '../seen.ts';
import { showToast } from '../toasts.ts';
import { copyText } from '../clipboard.ts';
import { Antiforgery, count } from './Forms.tsx';
import { Icon } from './Icon.tsx';
import { Menu } from './Menu.tsx';
import { Time } from './Time.tsx';

export interface CardPlacement {
  /** The beep the page is about, within its thread. */
  focused?: boolean;
  /** Shown inside its thread, so a reply needs no link to where it belongs. */
  inThread?: boolean;
  /** In a thread: the handle of the author it answers, when that beep is on the page. */
  replyingTo?: string | null;
  /** In an inbox entry, where seeing it leaves the notification unread (seen.ts). */
  inInbox?: boolean;
}

// The beeps whose like is on its way (a beep may be shown in more than one card).
const liking = new Set<string>();

// Clicks on these are their own, not the card's.
const OWN_CONTROLS = 'a, button, input, textarea, select, label, summary, details, form, dialog';

/** What the actions answer when they are sent in the background (Pages/Beep/Actions): a status code only. */
async function act(id: string, handler: string): Promise<boolean> {
  try {
    const response = await fetch(paths.beepAction(id, handler, here()), {
      method: 'POST',
      body: formData(),
      headers: { 'X-Requested-With': 'fetch' },
      credentials: 'same-origin',
    });
    return response.ok;
  } catch {
    return false;
  }
}

export function BeepCard({ id, focused, inThread, replyingTo, inInbox }: CardPlacement & { id: string }) {
  useShown(id);
  const seen = useSeen(id, !inInbox);
  const [menuOpen, setMenuOpen] = useState(false);
  const beep = beepSignal(id).value;
  if (!beep) return null;
  const author = beep.author;
  const profile = paths.profile(author.handle ?? '');
  const permalink = paths.beep(beep.id);
  const returnUrl = here();
  const member = viewer.value.member;

  // The whole card opens the beep, as on other social sites; its time is the real link, for keyboards and readers.
  const open = (event: TargetedMouseEvent<HTMLElement>): void => {
    if (focused || event.defaultPrevented || event.button !== 0 || !(event.target instanceof Element)) return;
    if (event.target.closest(OWN_CONTROLS) || window.getSelection()?.toString()) return;
    if (event.metaKey || event.ctrlKey) window.open(permalink, '_blank', 'noopener');
    else void visit(permalink).catch(() => {});
  };

  // Pressed at once, as on other social sites, and let go again if it did not go through.
  const like = async (event: TargetedSubmitEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault();
    if (liking.has(beep.id)) return;
    // Decided now: by the time the answer comes, a live event may already have pressed the button.
    const liked = !beep.liked;
    liking.add(beep.id);
    setLiked(beep.id, liked);
    const ok = await act(beep.id, liked ? 'Like' : 'Unlike');
    liking.delete(beep.id);
    if (ok) return;
    setLiked(beep.id, !liked);
    showToast(t('Error.ActionFailed'), true);
  };

  const remove = async (event: TargetedSubmitEvent<HTMLFormElement>): Promise<void> => {
    event.preventDefault();
    // The menu closes, as it would after picking anything in it.
    setMenuOpen(false);
    if (!(await confirmAction(t('Beep.Delete.Confirm')))) return;
    if (!(await act(beep.id, 'Delete'))) {
      showToast(t('Error.ActionFailed'), true);
      return;
    }
    removeBeep(beep.id);
    showToast(t('Beep.Deleted'));
    // The deleted beep's own page is gone with it.
    if (pageLive.value.subject === `beep:${beep.id}`) void visit(paths.home).catch(() => {});
  };

  // Copies the beep's address instead of opening it; where the clipboard cannot be written it stays a link.
  const share = (event: TargetedMouseEvent<HTMLAnchorElement>): void => {
    if (event.metaKey || event.ctrlKey || event.shiftKey) return;
    event.preventDefault();
    const url = event.currentTarget.href;
    void copyText(url).then((ok) => {
      if (ok) showToast(t('Beep.Share.Done'));
      else void visit(url).catch(() => {});
    });
  };

  return (
    <article ref={seen} class={focused ? 'beep beep-focused' : 'beep'} id={paths.beepAnchor(beep.id)} onClick={open}>
      <a class="beep-avatar" href={profile} tabIndex={-1} aria-hidden="true">
        <span class="avatar">
          <img src={author.avatar} alt="" width={40} height={40} loading="lazy" />
        </span>
      </a>
      <div class="beep-main">
        <header class="beep-header">
          <a class="beep-author" href={profile}>
            <span class="beep-name">{author.displayName ?? author.handle}</span>
            {author.agent && <span class="badge">{t('Beep.Agent')}</span>}
            <span class="beep-handle">@{author.handle}</span>
          </a>
          <span class="beep-dot" aria-hidden="true">
            ·
          </span>
          <a class="beep-time" href={permalink}>
            <Time value={beep.createdAt} />
          </a>
          {beep.underReview && (
            <span class="badge badge-warning" title={t('Beep.UnderReview.Help')}>
              {t('Beep.UnderReview')}
            </span>
          )}
          {(beep.canDelete || member) && (
            <Menu
              class="menu beep-menu"
              open={menuOpen}
              onOpenChange={setMenuOpen}
              summary={
                <summary class="beep-menu-toggle" aria-label={t('Beep.More')}>
                  <Icon name="more_horiz" />
                </summary>
              }
            >
              <div class="dropdown-menu menu-panel">
                {beep.canDelete ? (
                  <form class="beep-delete" method="post" action={paths.beepAction(beep.id, 'Delete', returnUrl)} data-confirm={t('Beep.Delete.Confirm')} onSubmit={remove}>
                    <button class="dropdown-item dropdown-item-danger" type="submit">
                      <Icon name="delete" />
                      {t('Beep.Delete')}
                    </button>
                    <Antiforgery />
                  </form>
                ) : (
                  <a class="dropdown-item beep-report" href={paths.report(beep.id)}>
                    <Icon name="flag" />
                    {t('Beep.Report')}
                  </a>
                )}
              </div>
            </Menu>
          )}
        </header>
        {beep.replyToId && replyingTo && (
          <p class="beep-context">
            <a href={`#${paths.beepAnchor(beep.replyToId)}`}>{t('Beep.ReplyingTo', replyingTo)}</a>
          </p>
        )}
        {beep.replyToId && !replyingTo && !inThread && (
          <p class="beep-context">
            <a href={paths.beep(beep.replyToId)}>{t('Beep.InReply')}</a>
          </p>
        )}
        {beep.html.length > 0 && <div class="beep-text" dangerouslySetInnerHTML={{ __html: beep.html }} />}
        {beep.media.length > 0 && (
          <div class={`beep-media beep-media-${beep.media.length}`}>
            {beep.media.map((image, i) => (
              <a class="beep-image" href={image.url} target="_blank" rel="noopener" key={image.url}>
                <img
                  src={image.thumbnail}
                  alt={t('Beep.Image', i + 1, beep.media.length)}
                  width={image.width}
                  height={image.height}
                  loading="lazy"
                  decoding="async"
                />
              </a>
            ))}
          </div>
        )}
        <footer class="beep-actions">
          <a class="beep-action beep-action-reply" href={`${permalink}#reply`}>
            <span class="beep-action-icon">
              <Icon name="chat_bubble" />
            </span>
            <span class="visually-hidden">{t('Beep.Replies')}</span>
            <span class="beep-count">{count(beep.replyCount)}</span>
          </a>
          {member ? (
            <form class="beep-like" method="post" action={paths.beepAction(beep.id, beep.liked ? 'Unlike' : 'Like', returnUrl)} onSubmit={like}>
              <button class="beep-action beep-action-like" type="submit" aria-pressed={beep.liked ? 'true' : 'false'}>
                <span class="beep-action-icon">
                  <Icon name="favorite" />
                </span>
                <span class="visually-hidden">{t('Beep.Like')}</span>
                <span class="beep-count">{count(beep.likeCount)}</span>
              </button>
              <Antiforgery />
            </form>
          ) : (
            <span class="beep-action beep-action-like">
              <span class="beep-action-icon">
                <Icon name="favorite" />
              </span>
              <span class="visually-hidden">{t('Beep.Likes', beep.likeCount)}</span>
              <span class="beep-count" aria-hidden="true">
                {count(beep.likeCount)}
              </span>
            </span>
          )}
          <a class="beep-action beep-action-share" href={permalink} title={t('Beep.Share')} onClick={share}>
            <span class="beep-action-icon">
              <Icon name="ios_share" />
            </span>
            <span class="visually-hidden">{t('Beep.Share')}</span>
          </a>
        </footer>
      </div>
    </article>
  );
}
