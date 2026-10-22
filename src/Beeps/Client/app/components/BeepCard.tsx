// One beep, drawn exactly as Pages/Shared/_Beep.cshtml draws it, from the beep's signal (beeps.ts): a new count, a
// like from another tab or a redrawn beep shows at once, and a beep that went disappears. A beep in another language
// shows its translation in place of its text, as on Twitter, with a line under it to show the original instead.
import type { TargetedMouseEvent, TargetedSubmitEvent } from 'preact';
import { useCallback, useState } from 'preact/hooks';
import { beepSignal, removeBeep, setLiked, useShown } from '../beeps.ts';
import { viewer } from '../config.ts';
import { confirmAction } from '../dialogs.ts';
import { formData } from '../http.ts';
import { t } from '../i18n.ts';
import { pageLive, showAnew } from '../page.ts';
import { useEnter } from '../motion.ts';
import { languageName } from '../languages.ts';
import { here, paths } from '../paths.ts';
import { visit } from '../router.ts';
import { useSeen } from '../seen.ts';
import { showToast } from '../toasts.ts';
import { copyText } from '../clipboard.ts';
import { showInLightbox, type LightboxImage } from '../lightbox.ts';
import { Antiforgery, count } from './Forms.tsx';
import { Icon } from './Icon.tsx';
import { Menu } from './Menu.tsx';
import { Time } from './Time.tsx';

interface CardPlacement {
  /** The beep the page is about, within its thread. */
  focused?: boolean;
  /** Shown inside its thread, so a reply needs no link to where it belongs. */
  inThread?: boolean;
  /** In a thread: the handle of the author it answers, when that beep is on the page. */
  replyingTo?: string | null;
  /** In an inbox entry, where seeing it leaves the notification unread (seen.ts). */
  inInbox?: boolean;
  /** It came into its list while the list was on screen: it plays its arrival (motion.ts) as it is first drawn. */
  entering?: boolean;
  /** Its arrival is over. */
  onEntered?: () => void;
}

// The beeps whose like is on its way (a beep may be shown in more than one card).
const liking = new Set<string>();

// Clicks on these are their own, not the card's.
const OWN_CONTROLS = 'a, button, input, textarea, select, label, summary, details, form, dialog';

/**
 * A click anywhere on a card opens `href`, as on other social sites (with Ctrl or ⌘, in a new tab): not one on the
 * card's own controls, or one that ends a selection of its text.
 */
export function openOnClick(event: TargetedMouseEvent<HTMLElement>, href: string): void {
  if (event.defaultPrevented || event.button !== 0 || !(event.target instanceof Element)) return;
  if (event.target.closest(OWN_CONTROLS) || window.getSelection()?.toString()) return;
  if (event.metaKey || event.ctrlKey) window.open(href, '_blank', 'noopener');
  else void visit(href).catch(() => {});
}

/** What the actions answer when they are sent in the background (Pages/Beep/Actions): a status code only. */
async function act(id: string, handler: string): Promise<boolean> {
  try {
    const response = await fetch(paths.beepAction(id, handler, here()), {
      method: 'POST',
      body: formData(),
      headers: { 'X-Requested-With': 'fetch' },
      credentials: 'same-origin',
      // Never followed: a redirect (to sign in, say) ends on a page that answers 200, as if it was done.
      redirect: 'manual',
    });
    // Signed out meanwhile, before the tab heard of it: the page shows that, as a reload would.
    if (response.status === 401 || response.type === 'opaqueredirect') void showAnew();
    return response.ok;
  } catch {
    return false;
  }
}

export function BeepCard({ id, focused, inThread, replyingTo, inInbox, entering, onEntered }: CardPlacement & { id: string }) {
  useShown(id);
  const seen = useSeen(id, !inInbox);
  const enter = useEnter(entering, onEntered);
  const card = useCallback(
    (element: HTMLElement | null) => {
      seen(element);
      enter(element);
    },
    [seen, enter],
  );
  const [menuOpen, setMenuOpen] = useState(false);
  const [original, setOriginal] = useState(false);
  const beep = beepSignal(id).value;
  if (!beep) return null;
  const author = beep.author;
  const profile = paths.profile(author.handle ?? '');
  const permalink = paths.beep(beep.id);
  const returnUrl = here();
  const member = viewer.value.member;
  const images: LightboxImage[] = beep.media.map((image, i) => ({
    src: image.url,
    alt: t('Beep.Image', i + 1, beep.media.length),
    width: image.width,
    height: image.height,
    thumbnail: image.thumbnail,
  }));

  // The whole card opens the beep; its time is the real link, for keyboards and readers.
  const open = (event: TargetedMouseEvent<HTMLElement>): void => {
    if (!focused) openOnClick(event, permalink);
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
    <article ref={card} class={focused ? 'beep beep-focused' : 'beep'} id={paths.beepAnchor(beep.id)} onClick={open}>
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
                  <form method="post" action={paths.beepAction(beep.id, 'Delete', returnUrl)} onSubmit={remove}>
                    <button class="dropdown-item dropdown-item-danger" type="submit">
                      <Icon name="delete" />
                      {t('Beep.Delete')}
                    </button>
                    <Antiforgery />
                  </form>
                ) : (
                  <a class="dropdown-item" href={paths.report(beep.id)}>
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
        {beep.html.length > 0 && (
          <div
            class="beep-text"
            lang={beep.translation && original ? beep.translation.lang : undefined}
            dangerouslySetInnerHTML={{ __html: beep.translation && !original ? beep.translation.html : beep.html }}
          />
        )}
        {beep.translation && (
          <p class="beep-translation">
            {t('Beep.Translated', languageName(beep.translation.lang, document.documentElement.lang))}
            <span class="beep-dot" aria-hidden="true">
              ·
            </span>
            <button class="beep-translation-toggle" type="button" onClick={() => setOriginal(!original)}>
              {original ? t('Beep.ShowTranslation') : t('Beep.ShowOriginal')}
            </button>
          </p>
        )}
        {beep.media.length > 0 && (
          <div class={`beep-media beep-media-${beep.media.length}`}>
            {beep.media.map((image, i) => (
              <a class="beep-image" href={image.url} target="_blank" rel="noopener" key={image.url} onClick={showInLightbox(images, i)}>
                <img
                  src={image.thumbnail}
                  alt={images[i]?.alt}
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
          <a class="beep-action" href={`${permalink}#reply`}>
            <span class="beep-action-icon">
              <Icon name="chat_bubble" />
            </span>
            <span class="visually-hidden">{t('Beep.Replies')}</span>
            <span class="beep-count">{count(beep.replyCount)}</span>
          </a>
          {member ? (
            <form method="post" action={paths.beepAction(beep.id, beep.liked ? 'Unlike' : 'Like', returnUrl)} onSubmit={like}>
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
              <span class="visually-hidden">{beep.likeCount === 1 ? t('Beep.Likes.One') : t('Beep.Likes', beep.likeCount)}</span>
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
