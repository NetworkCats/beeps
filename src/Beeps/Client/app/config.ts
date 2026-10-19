// What the app is told about the signed-in person and the page language (Pages/Islands/ClientConfig.cs), kept in the
// layout's <script type="application/json" id="app-config">: drawn there by the server, or, for an app shell, put
// there from the page's data (router.ts). Read again whenever a page is shown, since the person may have changed
// meanwhile (a new avatar).
import { signal } from '@preact/signals';

/** Who is signed in; signed out, `human` is false and the rest empty. */
export interface Viewer {
  human: boolean;
  id: string;
  handle: string;
  avatar: string | null;
  /** Verified: may post, like, follow. */
  member: boolean;
  admin: boolean;
}

export interface Limits {
  beepLength: number;
  imagesPerBeep: number;
  mediaBytes: number;
}

export interface AppConfig {
  /** Where the interface text in the page's language is (i18n.ts). */
  stringsUrl: string;
  antiforgery: { field: string; token: string };
  viewer: Viewer;
  limits: Limits;
}

const EMPTY: AppConfig = {
  stringsUrl: '',
  antiforgery: { field: '', token: '' },
  viewer: { human: false, id: '', handle: '', avatar: null, member: false, admin: false },
  limits: { beepLength: 0, imagesPerBeep: 0, mediaBytes: 0 },
};

let current: AppConfig = EMPTY;

/** The viewer as last heard of: the layout's config, then live changes (a new avatar). */
export const viewer = signal<Viewer>(EMPTY.viewer);

export function config(): AppConfig {
  return current;
}

export function readConfig(root: ParentNode = document): void {
  const script = root.querySelector('script#app-config');
  if (script?.textContent) setConfig(JSON.parse(script.textContent) as AppConfig);
}

/**
 * A page gone back to shows the viewer as last heard of (a new avatar since), not as they were when it was left: its
 * saved config is given the viewer of now, before it is read.
 */
export function keepViewer(root: ParentNode = document): void {
  const script = root.querySelector('script#app-config');
  if (!script?.textContent) return;
  const saved = JSON.parse(script.textContent) as AppConfig;
  if (saved.viewer.id === viewer.value.id) script.textContent = JSON.stringify({ ...saved, viewer: viewer.value });
}

export function setConfig(config: AppConfig): void {
  current = config;
  viewer.value = config.viewer;
}
