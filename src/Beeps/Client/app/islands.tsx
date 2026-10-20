// The islands of a page (Pages/Islands/IslandTagHelper.cs): <div class="island" data-island="name"> with its props.
// The Preact component registered under its name draws it from them and keeps it up to date from then on. Signed out
// the server has drawn the markup inside as well (search engines read it), and the component draws the same over it;
// signed in a page's islands arrive empty in its data, their props beside them (router.ts). New props for an island
// (it caught up, a form it holds was answered) go into its envelope signal, and the component draws itself again.
//
// The islands of the layout that every page has come with the app; each page's own component is a chunk of its own
// (lazyIsland), loaded when a page first has it. The server names those chunks in the page (modulepreload), so they
// load alongside the app, and once a page has loaded the app fetches all the others ahead (preloadIslands). The admin
// pages' (adminIsland) are fetched ahead for admins only, and the server gives them to no one else.
import { createContext, render, type ComponentType } from 'preact';
import { useContext, useEffect } from 'preact/hooks';
import { signal, type Signal } from '@preact/signals';
import type { Envelope, Errors, IslandsPage } from './props.ts';

export interface IslandProps<P> {
  props: P;
  errors: Errors;
}

type Component = ComponentType<IslandProps<any>>;

interface Mounted {
  name: string;
  element: HTMLElement;
  envelope: Signal<Envelope>;
  /** Drawn: its component has loaded (until then a signed-out island shows the server's markup). */
  drawn?: boolean;
  /** The island's state as it is now (a list that loaded more pages), for going back to the page. */
  snapshot?: () => unknown;
}

interface Lazy {
  load: () => Promise<Component>;
  admin: boolean;
}

const components = new Map<string, Component>();
const lazy = new Map<string, Lazy>();
const loading = new Map<string, Promise<Component>>();
let mounted: Mounted[] = [];

const IslandContext = createContext<Mounted | null>(null);

/** An island whose component comes with the app. */
export function island<P>(name: string, component: ComponentType<IslandProps<P>>): void {
  components.set(name, component);
}

/**
 * An island whose component is a chunk of its own, loaded when it is first needed. `load` is a plain
 * `() => import('./File.tsx').then((m) => m.Export)`: scripts/build-client.ts reads it to name the chunk to the server.
 */
export function lazyIsland<P>(name: string, load: () => Promise<ComponentType<IslandProps<P>>>): void {
  lazy.set(name, { load, admin: false });
}

/** A lazy island of the admin pages: its chunk is for admins only (ClientAssetsMiddleware on the server). */
export function adminIsland<P>(name: string, load: () => Promise<ComponentType<IslandProps<P>>>): void {
  lazy.set(name, { load, admin: true });
}

/** The component registered for an island's name, its chunk loaded first when it has not been yet. */
export function loadComponent(name: string): Promise<Component | undefined> {
  const component = components.get(name);
  if (component) return Promise.resolve(component);
  const entry = lazy.get(name);
  if (!entry) return Promise.resolve(undefined);
  let pending = loading.get(name);
  if (!pending) {
    pending = entry.load().then((loaded) => {
      components.set(name, loaded);
      return loaded;
    });
    loading.set(name, pending);
    // Offline for a moment: the next page that has it tries again.
    pending.catch(() => loading.delete(name));
  }
  return pending;
}

/** Loads the components of all islands ahead of the pages that have them; the admin pages' only for an admin. */
export function preloadIslands(admin: boolean): void {
  for (const [name, entry] of lazy) {
    if (admin || !entry.admin) loadComponent(name).catch(() => {});
  }
}

function Host({ component: Component, envelope }: { component: ComponentType<IslandProps<unknown>>; envelope: Signal<Envelope> }) {
  const { props, errors } = envelope.value;
  return <Component props={props} errors={errors ?? {}} />;
}

/** The island's props, taken out of the page: what the component draws is all that stays in the island. */
function read(element: HTMLElement): Envelope | null {
  const script = element.querySelector(':scope > script[data-island-props]');
  if (!script?.textContent) return null;
  script.remove();
  try {
    return JSON.parse(script.textContent) as Envelope;
  } catch {
    return null;
  }
}

function draw(record: Mounted, component: Component): void {
  record.drawn = true;
  // The skeleton that stood in for it (Pages/Shared/_Skeleton.cshtml) goes: Preact only takes over markup it would draw.
  record.element.querySelector(':scope > .skeleton-page')?.remove();
  // An island that throws as it is drawn is that island's fault alone: the others are still drawn, and it does not
  // count as a chunk that failed to load, which loads the page in full (where it would throw again, and again).
  try {
    render(
      <IslandContext.Provider value={record}>
        <Host component={component} envelope={record.envelope} />
      </IslandContext.Provider>,
      record.element,
    );
  } catch (error) {
    console.error(error);
  }
}

/**
 * Takes over the islands under `root`; `saved` holds props to draw instead (a page gone back to, as it was left).
 * Islands whose component is loaded already are drawn at once, the others as their chunks arrive; the promise settles
 * once all are drawn, and fails when a chunk could not be loaded.
 */
export function mountIslands(root: ParentNode, saved?: ReadonlyMap<string, Envelope>): Promise<void> {
  const waiting: Array<Promise<void>> = [];
  const key = keyer();
  for (const element of root.querySelectorAll<HTMLElement>('[data-island]')) {
    const name = element.dataset.island ?? '';
    if (!components.has(name) && !lazy.has(name)) continue;
    const envelope = saved?.get(key(name)) ?? read(element);
    if (!envelope) continue;
    const record: Mounted = { name, element, envelope: signal(envelope) };
    mounted.push(record);
    const component = components.get(name);
    if (component) {
      draw(record, component);
      continue;
    }
    waiting.push(loadComponent(name).then((loaded) => {
      // Another page may have been shown meanwhile.
      if (loaded && mounted.includes(record)) draw(record, loaded);
    }));
  }
  return Promise.all(waiting).then(() => {});
}

/** Lets the islands (under `root`) go, their effects end: what they were on is about to be replaced. */
export function unmountIslands(root?: Node): void {
  const leaving = root ? mounted.filter((record) => root.contains(record.element)) : mounted;
  for (const record of leaving) {
    if (record.drawn) render(null, record.element);
  }
  mounted = mounted.filter((record) => !leaving.includes(record));
}

/** New props for the islands the page names, from an islands request. */
export function applyIslands(page: IslandsPage): void {
  for (const record of mounted) {
    const envelope = page.islands[record.name];
    if (envelope) record.envelope.value = envelope;
  }
}

/**
 * What an island's props are saved under: its name, and after the first its place among those of that name (the
 * layout draws some twice, the top bar's and the side column's, each from props of its own).
 */
function keyer(): (name: string) => string {
  const seen = new Map<string, number>();
  return (name) => {
    const count = seen.get(name) ?? 0;
    seen.set(name, count + 1);
    return count === 0 ? name : `${name}#${count}`;
  };
}

/** Each island's props as it is drawn now, for drawing the page again as it was left. */
export function snapshotIslands(): Map<string, Envelope> {
  const saved = new Map<string, Envelope>();
  const key = keyer();
  // In the order of the page, as mountIslands goes through it.
  const ordered = [...mounted].sort((a, b) => (a.element.compareDocumentPosition(b.element) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1));
  for (const record of ordered) {
    const props = record.snapshot ? record.snapshot() : record.envelope.value.props;
    saved.set(key(record.name), { props, errors: record.envelope.value.errors });
  }
  return saved;
}

/** An island whose state goes beyond its props (more pages loaded) says what to draw it from when it is gone back to. */
export function useSnapshot(snapshot: () => unknown): void {
  const record = useContext(IslandContext);
  useEffect(() => {
    if (!record) return;
    record.snapshot = snapshot;
    return () => {
      if (record.snapshot === snapshot) record.snapshot = undefined;
    };
  });
}
