// The lightbox (lightbox.ts), in the layout's <dialog>: the picture as large as the screen allows on a dark page, as
// Twitter shows one. It grows out of the thumbnail it was opened from and shrinks back into it.
//
// The pictures of a beep sit side by side on a track: swiped, or the arrows and arrow keys, go through them. A picture
// zooms with a pinch, the wheel, a double tap or click, and + / - / 0; zoomed, it is dragged around. Unzoomed, dragging
// it up or down closes it, as do Escape, the close button and a tap beside it. The script takes every touch
// (touch-action: none), so the page under it never zooms or scrolls; the moves are drawn straight onto the elements, not
// through Preact, so they keep up with the finger.
import type { CSSProperties } from 'preact';
import { useLayoutEffect, useRef } from 'preact/hooks';
import { t } from '../i18n.ts';
import { lightbox, lightboxReady } from '../lightbox.ts';
import { clampView, fromThumbnail, IDENTITY, viewTransform, zoomAt, type View } from '../lightbox-view.ts';
import { Icon } from '../components/Icon.tsx';

const OPEN_TIME = 300;
const CLOSE_TIME = 220;
// --cui-ease-out and --cui-ease: things arrive fast and settle long, and leave quicker.
const EASE_OUT = 'cubic-bezier(0.16, 1, 0.3, 1)';
const EASE = 'cubic-bezier(0.2, 0, 0, 1)';
// How far a press moves before it is a drag, and how fast a drag ends for a flick (px per ms).
const SLOP = 8;
const FLICK = 0.5;
// How far a picture is dragged up or down to close it.
const DISMISS = 120;
const DOUBLE_TAP = 300;
const DOUBLE_TAP_ZOOM = 2.5;

type Mode = 'idle' | 'press' | 'pan' | 'pinch' | 'swipe' | 'dismiss';

interface Point {
  x: number;
  y: number;
}

interface Gesture {
  mode: Mode;
  pointers: Map<number, Point>;
  /** What the first finger went down on, and where. */
  target: EventTarget | null;
  start: Point;
  /** The zoom when the gesture began. */
  view: View;
  /** A pinch's fingers when it began: how far apart, and the point between them. */
  distance: number;
  middle: Point;
  last: Point & { time: number };
  velocity: Point;
  /** The last tap, for a double tap. */
  tap: (Point & { time: number }) | null;
}

function reducedMotion(): boolean {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/** Laid out and inside the window: a thumbnail the picture can grow out of. */
function onScreen(element: Element | null | undefined): element is Element {
  if (!element?.isConnected) return false;
  const box = element.getBoundingClientRect();
  return box.width > 0 && box.height > 0 && box.bottom > 0 && box.right > 0 && box.top < window.innerHeight && box.left < window.innerWidth;
}

function between(a: Point, b: Point): Point {
  return { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 };
}

function step(by: number): void {
  const open = lightbox.value;
  if (!open) return;
  const index = open.index + by;
  if (index >= 0 && index < open.images.length) lightbox.value = { ...open, index };
}

export function Lightbox() {
  const dialog = useRef<HTMLDialogElement>(null);
  const shade = useRef<HTMLDivElement>(null);
  const track = useRef<HTMLDivElement>(null);
  const view = useRef<View>(IDENTITY);
  const gesture = useRef<Gesture>({
    mode: 'idle',
    pointers: new Map(),
    target: null,
    start: { x: 0, y: 0 },
    view: IDENTITY,
    distance: 1,
    middle: { x: 0, y: 0 },
    last: { x: 0, y: 0, time: 0 },
    velocity: { x: 0, y: 0 },
    tap: null,
  });
  // The animations of a closing lightbox, cancelled once it has closed (they hold their end until then).
  const closing = useRef<Animation[] | null>(null);
  const open = lightbox.value;
  const shown = open !== null;
  const index = open?.index ?? 0;

  const picture = (): HTMLImageElement | null => track.current?.children[lightbox.peek()?.index ?? 0]?.querySelector('img') ?? null;

  // While a finger or the mouse moves things they follow it at once; let go, they ease to where they belong.
  const setDragging = (dragging: boolean): void => {
    dialog.current?.toggleAttribute('data-dragging', dragging);
  };

  const showView = (next: View): void => {
    view.current = next;
    const image = picture();
    if (image) image.style.transform = next.scale === 1 && next.x === 0 && next.y === 0 ? '' : viewTransform(next);
    dialog.current?.toggleAttribute('data-zoomed', next.scale > 1);
  };

  const fitted = (next: View): View => {
    const image = picture();
    const box = dialog.current;
    return image && box ? clampView(next, image.offsetWidth, image.offsetHeight, box.clientWidth, box.clientHeight) : IDENTITY;
  };

  const centre = (): Point => ({ x: (dialog.current?.clientWidth ?? 0) / 2, y: (dialog.current?.clientHeight ?? 0) / 2 });

  const zoomTo = (scale: number, at: Point): void => {
    const middle = centre();
    showView(fitted(zoomAt(view.current, scale, at.x, at.y, middle.x, middle.y)));
  };

  // The track, on the current picture, pulled aside by `offset` while it is swiped.
  const slide = (offset: number): void => {
    if (track.current) track.current.style.transform = `translateX(calc(${-(lightbox.peek()?.index ?? 0) * 100}% + ${offset}px))`;
  };

  // Dragged up or down to close: the picture follows, and the page shows through more the further it goes.
  const lift = (offset: number): void => {
    const image = picture();
    if (image) image.style.transform = offset ? `translateY(${offset}px)` : '';
    const fade = Math.min(1, Math.abs(offset) / 400);
    if (shade.current) shade.current.style.opacity = offset ? String(1 - fade) : '';
    dialog.current?.style.setProperty('--lightbox-chrome', offset ? String(Math.max(0, 1 - fade * 3)) : '1');
  };

  const close = (): void => {
    const element = dialog.current;
    const current = lightbox.peek();
    if (!element || !current || closing.current) return;
    // What is easing is cut short, so the picture is measured where it will be.
    setDragging(true);
    const options: KeyframeAnimationOptions = { duration: reducedMotion() ? 0 : CLOSE_TIME, easing: EASE, fill: 'forwards' };
    const animations: Animation[] = [];
    if (shade.current) animations.push(shade.current.animate([{ opacity: getComputedStyle(shade.current).opacity }, { opacity: 0 }], options));
    for (const part of element.querySelectorAll('.lightbox-chrome')) {
      animations.push(part.animate([{ opacity: getComputedStyle(part).opacity }, { opacity: 0 }], options));
    }
    const image = picture();
    if (image) {
      const now = image.style.transform || 'none';
      const thumbnail = current.thumbnails[current.index];
      if (onScreen(thumbnail)) {
        image.style.transform = '';
        const back = fromThumbnail(thumbnail.getBoundingClientRect(), image.getBoundingClientRect());
        image.style.transform = now === 'none' ? '' : now;
        animations.push(image.animate([{ transform: now, clipPath: 'inset(0px 0px)' }, back], options));
      } else {
        animations.push(image.animate([{ transform: now, opacity: 1 }, { transform: `${now === 'none' ? '' : now} scale(0.9)`, opacity: 0 }], options));
      }
    }
    closing.current = animations;
    void Promise.all(animations.map((animation) => animation.finished))
      .catch(() => {})
      .then(() => element.close());
  };

  useLayoutEffect(() => {
    lightboxReady.value = typeof dialog.current?.showModal === 'function';
    return () => {
      lightboxReady.value = false;
      // A page that is left takes its pictures with it.
      lightbox.value = null;
    };
  }, []);

  // Another picture: the track moves to it, and the zoom goes with the one that was shown.
  useLayoutEffect(() => {
    if (!open) return;
    for (const image of track.current?.querySelectorAll('img') ?? []) image.style.transform = '';
    view.current = IDENTITY;
    dialog.current?.removeAttribute('data-zoomed');
    slide(0);
  }, [index]);

  // Opened: the picture grows out of its thumbnail (or fades in, without one) as the page darkens behind it.
  useLayoutEffect(() => {
    const element = dialog.current;
    const current = lightbox.peek();
    if (!element || !current) return;
    const closed = (): void => {
      for (const animation of closing.current ?? []) animation.cancel();
      closing.current = null;
      gesture.current.pointers.clear();
      gesture.current.mode = 'idle';
      lightbox.value = null;
    };
    element.addEventListener('close', closed, { once: true });
    view.current = IDENTITY;
    element.removeAttribute('data-zoomed');
    setDragging(false);
    lift(0);
    slide(0);
    element.showModal();

    const options: KeyframeAnimationOptions = { duration: reducedMotion() ? 0 : OPEN_TIME, easing: EASE_OUT };
    shade.current?.animate([{ opacity: 0 }, { opacity: 1 }], options);
    for (const part of element.querySelectorAll('.lightbox-chrome')) part.animate([{ opacity: 0 }, { opacity: 1 }], options);
    const image = picture();
    const thumbnail = current.thumbnails[current.index];
    if (image && onScreen(thumbnail)) {
      image.animate([fromThumbnail(thumbnail.getBoundingClientRect(), image.getBoundingClientRect()), { transform: 'none', clipPath: 'inset(0px 0px)' }], options);
    } else {
      image?.animate([{ opacity: 0, transform: 'scale(0.95)' }, { opacity: 1, transform: 'none' }], options);
    }
    return () => {
      element.removeEventListener('close', closed);
      if (element.open) element.close();
    };
  }, [shown]);

  const pointerDown = (event: PointerEvent): void => {
    const g = gesture.current;
    if (closing.current || (event.target as Element).closest('button') || (event.pointerType === 'mouse' && event.button !== 0)) return;
    dialog.current?.setPointerCapture(event.pointerId);
    const point = { x: event.clientX, y: event.clientY };
    g.pointers.set(event.pointerId, point);
    if (g.pointers.size === 1) {
      g.mode = 'press';
      g.target = event.target;
      g.start = point;
      g.view = view.current;
      g.last = { ...point, time: event.timeStamp };
      g.velocity = { x: 0, y: 0 };
    } else {
      // Another finger: what the first was doing gives way to a pinch.
      slide(0);
      if (g.mode === 'dismiss') lift(0);
      const [a, b] = [...g.pointers.values()] as [Point, Point];
      g.mode = 'pinch';
      g.distance = Math.hypot(a.x - b.x, a.y - b.y) || 1;
      g.middle = between(a, b);
      g.view = view.current;
    }
    setDragging(true);
  };

  const pointerMove = (event: PointerEvent): void => {
    const g = gesture.current;
    if (!g.pointers.has(event.pointerId)) return;
    const point = { x: event.clientX, y: event.clientY };
    g.pointers.set(event.pointerId, point);

    if (g.mode === 'pinch') {
      const [a, b] = [...g.pointers.values()] as [Point, Point];
      const middle = between(a, b);
      // A little past either end while the fingers are down; it eases back once they lift.
      const scale = Math.min(6, Math.max(0.6, (g.view.scale * Math.hypot(a.x - b.x, a.y - b.y)) / g.distance));
      const screen = centre();
      // Zoomed about where the fingers began, then moved along with them.
      const zoomed = zoomAt(g.view, scale, g.middle.x, g.middle.y, screen.x, screen.y);
      showView({ scale, x: zoomed.x + middle.x - g.middle.x, y: zoomed.y + middle.y - g.middle.y });
      return;
    }

    const elapsed = event.timeStamp - g.last.time;
    if (elapsed > 0) g.velocity = { x: (point.x - g.last.x) / elapsed, y: (point.y - g.last.y) / elapsed };
    g.last = { ...point, time: event.timeStamp };
    const dx = point.x - g.start.x;
    const dy = point.y - g.start.y;
    if (g.mode === 'press') {
      if (Math.hypot(dx, dy) < SLOP) return;
      g.mode = view.current.scale > 1 ? 'pan' : Math.abs(dx) > Math.abs(dy) ? 'swipe' : 'dismiss';
    }
    if (g.mode === 'pan') {
      showView(fitted({ ...g.view, x: g.view.x + dx, y: g.view.y + dy }));
    } else if (g.mode === 'swipe') {
      const current = lightbox.peek();
      // Past the first or the last picture the track gives only a little.
      const end = !current || (dx > 0 ? current.index === 0 : current.index === current.images.length - 1);
      slide(end ? dx / 3 : dx);
    } else if (g.mode === 'dismiss') {
      lift(dy);
    }
  };

  const tap = (point: Point, time: number): void => {
    const g = gesture.current;
    const last = g.tap;
    if (last && time - last.time < DOUBLE_TAP && Math.hypot(point.x - last.x, point.y - last.y) < 32) {
      g.tap = null;
      if (view.current.scale > 1) showView(IDENTITY);
      else zoomTo(DOUBLE_TAP_ZOOM, point);
      return;
    }
    g.tap = { ...point, time };
    if (!(g.target instanceof Element && g.target.closest('.lightbox-image'))) close();
  };

  const pointerUp = (event: PointerEvent): void => {
    const g = gesture.current;
    if (!g.pointers.delete(event.pointerId)) return;
    if (g.mode === 'pinch' && g.pointers.size > 0) {
      // One finger left: it carries on moving the picture.
      const [rest] = [...g.pointers.values()] as [Point];
      g.mode = 'pan';
      g.start = rest;
      g.view = view.current;
      return;
    }
    if (g.pointers.size > 0) return;
    const mode = g.mode;
    g.mode = 'idle';
    setDragging(false);
    const dx = event.clientX - g.start.x;
    const dy = event.clientY - g.start.y;

    if (mode === 'press' && event.type === 'pointerup') {
      tap({ x: event.clientX, y: event.clientY }, event.timeStamp);
    } else if (mode === 'pinch' || mode === 'pan') {
      showView(fitted(view.current));
    } else if (mode === 'swipe') {
      const current = lightbox.peek();
      const width = dialog.current?.clientWidth ?? 1;
      const by = dx < -width / 5 || g.velocity.x < -FLICK ? 1 : dx > width / 5 || g.velocity.x > FLICK ? -1 : 0;
      const next = (current?.index ?? 0) + by;
      if (by !== 0 && current && next >= 0 && next < current.images.length) step(by);
      else slide(0);
    } else if (mode === 'dismiss') {
      if (Math.abs(dy) > DISMISS || Math.abs(g.velocity.y) > FLICK) close();
      else lift(0);
    }
  };

  const wheel = (event: WheelEvent): void => {
    event.preventDefault();
    if (closing.current) return;
    const delta = event.deltaY * (event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? 400 : 1);
    // A trackpad's pinch comes as wheel events with ctrlKey, many and fine; a wheel's come in notches.
    zoomTo(view.current.scale * Math.exp(-delta * (event.ctrlKey ? 0.01 : 0.0025)), { x: event.clientX, y: event.clientY });
  };

  const image = open?.images[index];

  return (
    <dialog
      class="lightbox"
      ref={dialog}
      aria-label={image?.alt}
      onCancel={(event) => {
        event.preventDefault();
        close();
      }}
      onKeyDown={(event) => {
        if (event.key === 'ArrowLeft') step(-1);
        else if (event.key === 'ArrowRight') step(1);
        else if (event.key === '+' || event.key === '=') zoomTo(view.current.scale * 1.5, centre());
        else if (event.key === '-') zoomTo(view.current.scale / 1.5, centre());
        else if (event.key === '0') showView(IDENTITY);
      }}
      onPointerDown={pointerDown}
      onPointerMove={pointerMove}
      onPointerUp={pointerUp}
      onPointerCancel={pointerUp}
      onWheel={wheel}
    >
      <div class="lightbox-shade" ref={shade} />
      <div class="lightbox-track" ref={track}>
        {open?.images.map((item, i) => (
          <div class="lightbox-slide" key={i}>
            {/* The pictures either side are there too, loaded ahead for a swipe. */}
            {Math.abs(i - index) <= 1 && (
              <img
                key={item.src}
                class={item.round ? 'lightbox-image lightbox-round' : 'lightbox-image'}
                src={item.src}
                alt={item.alt}
                style={
                  {
                    '--lightbox-ratio': String(item.width / item.height),
                    backgroundImage: item.thumbnail ? `url("${item.thumbnail}")` : undefined,
                  } as CSSProperties
                }
                draggable={false}
              />
            )}
          </div>
        ))}
      </div>
      <button class="lightbox-button lightbox-close lightbox-chrome" type="button" aria-label={t('Lightbox.Close')} onClick={close} autofocus>
        <Icon name="close" />
      </button>
      {open && index > 0 && (
        <button class="lightbox-button lightbox-previous lightbox-chrome" type="button" aria-label={t('Lightbox.Previous')} onClick={() => step(-1)}>
          <Icon name="arrow_back" />
        </button>
      )}
      {open && index < open.images.length - 1 && (
        <button class="lightbox-button lightbox-next lightbox-chrome" type="button" aria-label={t('Lightbox.Next')} onClick={() => step(1)}>
          <Icon name="arrow_forward" />
        </button>
      )}
      {open && open.images.length > 1 && (
        <div class="lightbox-dots lightbox-chrome" aria-hidden="true">
          {open.images.map((_, i) => (
            <span key={i} aria-current={i === index ? 'true' : undefined} />
          ))}
        </div>
      )}
    </dialog>
  );
}
