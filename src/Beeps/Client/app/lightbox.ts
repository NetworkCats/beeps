// A picture shown over the page at full size (shell/Lightbox.tsx): a beep's images, a profile's avatar or banner. What
// opens it stays a link to the image, so without the app (or with a modifier key) the image opens in a tab of its own.
import type { TargetedMouseEvent } from 'preact';
import { signal } from '@preact/signals';

export interface LightboxImage {
  src: string;
  alt: string;
  /** Its size, or only its shape: it is laid out before it has loaded. */
  width: number;
  height: number;
  /** Shown under the image while it loads, when there is a smaller one already on the page. */
  thumbnail?: string;
  /** An avatar, drawn round as it is everywhere else. */
  round?: boolean;
}

export interface OpenLightbox {
  images: LightboxImage[];
  index: number;
  /** The thumbnails on the page, one for each image where there is one: the picture grows out of it and back. */
  thumbnails: Array<Element | null>;
}

export const lightbox = signal<OpenLightbox | null>(null);

/** Whether a lightbox is on the page to show pictures in. */
export const lightboxReady = { value: false };

/**
 * A click handler for a link to an image: it shows `images` from `index` on instead, unless asked for a new tab. Of
 * several images, each link is one of its parent's children, in order (a beep's .beep-media).
 */
export function showInLightbox(images: LightboxImage[], index = 0) {
  return (event: TargetedMouseEvent<HTMLAnchorElement>): void => {
    if (!lightboxReady.value || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    const link = event.currentTarget;
    const links = images.length > 1 && link.parentElement ? [...link.parentElement.children] : [link];
    lightbox.value = { images, index, thumbnails: images.map((_, i) => links[images.length > 1 ? i : 0]?.querySelector('img') ?? null) };
  };
}
