// The geometry of the lightbox (shell/Lightbox.tsx), kept apart from the DOM so it can be tested: where a zoomed picture
// sits, how far it may be dragged, and how it grows out of the thumbnail it was opened from.

/** A picture's zoom: scaled about its centre, then moved by x and y (CSS pixels). */
export interface View {
  scale: number;
  x: number;
  y: number;
}

export interface Box {
  left: number;
  top: number;
  width: number;
  height: number;
}

export const IDENTITY: View = { scale: 1, x: 0, y: 0 };
export const MAX_SCALE = 4;

export function viewTransform(view: View): string {
  return `translate(${view.x}px, ${view.y}px) scale(${view.scale})`;
}

/**
 * The view at `scale` that keeps the point under (px, py) where it is. (cx, cy) is the picture's centre unzoomed, in
 * the same coordinates.
 */
export function zoomAt(view: View, scale: number, px: number, py: number, cx: number, cy: number): View {
  const ratio = scale / view.scale;
  return { scale, x: px - cx - (px - cx - view.x) * ratio, y: py - cy - (py - cy - view.y) * ratio };
}

/**
 * The view held to what the screen can show: no smaller than the picture fits, no larger than MAX_SCALE, and moved no
 * further than to bring an edge of the picture (width × height unzoomed, centred on the screen) to the screen's edge.
 */
export function clampView(view: View, width: number, height: number, screenWidth: number, screenHeight: number): View {
  const scale = Math.min(MAX_SCALE, Math.max(1, view.scale));
  const reachX = Math.max(0, (width * scale - screenWidth) / 2);
  const reachY = Math.max(0, (height * scale - screenHeight) / 2);
  const clamp = (value: number, reach: number): number => Math.min(reach, Math.max(-reach, value)) || 0;
  return { scale, x: clamp(view.x, reachX), y: clamp(view.y, reachY) };
}

/**
 * How the picture laid out at `to` is moved, scaled and cut to look like the thumbnail at `from`, which shows it
 * filling its box (object-fit: cover): the transform, and the clip-path that cuts away what the thumbnail crops.
 */
export function fromThumbnail(from: Box, to: Box): { transform: string; clipPath: string } {
  const scale = Math.max(from.width / to.width, from.height / to.height);
  const x = from.left + from.width / 2 - (to.left + to.width / 2);
  const y = from.top + from.height / 2 - (to.top + to.height / 2);
  // In the picture's own pixels, before it is scaled.
  const insetX = Math.max(0, (to.width * scale - from.width) / 2 / scale);
  const insetY = Math.max(0, (to.height * scale - from.height) / 2 / scale);
  return { transform: `translate(${x}px, ${y}px) scale(${scale})`, clipPath: `inset(${insetY}px ${insetX}px)` };
}
