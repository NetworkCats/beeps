// The link to a list's next page (<nav class="pager">), as the server draws it. Here the next page loads as
// the link nears the bottom of the screen (or when it is clicked); a spinner stands in for the link meanwhile, which
// comes back only when loading failed, to try again.
import { useEffect, useRef, useState } from 'preact/hooks';
import { t } from '../i18n.ts';

interface PagerProps {
  href: string;
  label: string;
  /** Loads the next page into the list; false when it could not. */
  onMore: () => Promise<boolean>;
  /** The admin tables' pagers have their own label and small buttons. */
  small?: boolean;
  ariaLabel?: string;
}

export function Pager({ href, label, onMore, small, ariaLabel }: PagerProps) {
  const element = useRef<HTMLElement>(null);
  const [state, setState] = useState<'idle' | 'loading' | 'failed'>('idle');
  const running = useRef(false);

  const load = async (): Promise<void> => {
    if (running.current) return;
    running.current = true;
    setState('loading');
    const ok = await onMore().catch(() => false);
    running.current = false;
    // Loaded, this pager goes, or is the next page's now (drawn anew under its own key).
    setState(ok ? 'idle' : 'failed');
  };

  useEffect(() => {
    const target = element.current;
    if (!target) return;
    // A screen ahead, so the next page is there before it is scrolled to.
    const observer = new IntersectionObserver((entries) => {
      if (entries.some((entry) => entry.isIntersecting)) void load();
    }, { rootMargin: '0px 0px 100% 0px' });
    observer.observe(target);
    return () => observer.disconnect();
  }, [href]);

  return (
    <nav class="pager" data-more-state={state} aria-busy={state === 'loading' ? 'true' : undefined} aria-label={ariaLabel ?? t('Pager.Label')} ref={element}>
      <a
        class={small ? 'btn btn-sm' : 'btn'}
        href={href}
        onClick={(event) => {
          event.preventDefault();
          void load();
        }}
      >
        {label}
      </a>
    </nav>
  );
}
