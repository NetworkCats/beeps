// How a card that arrives in a list on screen shows up: the room for it opens, pushing what is below down rather than
// jumping it, and the card fades in as it settles into place. The timing is collection-ui's (--cui-duration-slower for
// the room, --cui-duration-slow for the card, --cui-ease-out for both), so people who ask for less motion see it simply
// appear: the tokens are 0ms for them. A tab nobody is looking at does not animate either.
import { useCallback, useRef } from 'preact/hooks';

// How far above its place the card starts, as it fades in.
const RISE = '-0.5rem';

function token(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
}

function milliseconds(value: string): number {
  const number = Number.parseFloat(value);
  if (!Number.isFinite(number)) return 0;
  return value.endsWith('ms') ? number : number * 1000;
}

/** Plays the arrival on an element just put on the page (before it is painted), then leaves its styles as they were. */
export async function enter(element: HTMLElement): Promise<void> {
  const open = milliseconds(token('--cui-duration-slower'));
  const fade = milliseconds(token('--cui-duration-slow'));
  if (document.hidden || open <= 0 || typeof element.animate !== 'function') return;
  const easing = token('--cui-ease-out') || 'ease-out';
  const style = getComputedStyle(element);
  const height = element.getBoundingClientRect().height;
  // The room opens from nothing: its height, padding and dividing line all start at zero, and what it holds is
  // clipped meanwhile.
  const closed = { height: '0px', paddingTop: '0px', paddingBottom: '0px', borderBottomWidth: '0px' };
  const opened = {
    height: `${height}px`,
    paddingTop: style.paddingTop,
    paddingBottom: style.paddingBottom,
    borderBottomWidth: style.borderBottomWidth,
  };
  const overflow = element.style.overflow;
  element.style.overflow = 'clip';
  const animations = [
    element.animate([closed, opened], { duration: open, easing }),
    // Fades in once the room has begun to open, so it never shows squeezed.
    element.animate([{ opacity: 0, translate: `0 ${RISE}` }, { opacity: 1, translate: '0 0' }], {
      duration: fade,
      delay: open - fade,
      easing,
      fill: 'backwards',
    }),
  ];
  await Promise.allSettled(animations.map((animation) => animation.finished));
  element.style.overflow = overflow;
}

/**
 * A ref for an element that should play its arrival: only when it was `entering` as it was first drawn, and only once,
 * however often it is drawn again. `entered` is told when the arrival is over (or was not played).
 */
export function useEnter(entering: boolean | undefined, entered?: () => void): (element: HTMLElement | null) => void {
  const pending = useRef(!!entering);
  const done = useRef(entered);
  done.current = entered;
  return useCallback((element: HTMLElement | null) => {
    if (!element || !pending.current) return;
    pending.current = false;
    void enter(element).finally(() => done.current?.());
  }, []);
}
