// The islands of a page (Pages/Islands/IslandTagHelper.cs): <div class="island" data-island="name"> with its props.
// The Preact component registered under its name draws it from them and keeps it up to date from then on. Signed out
// the server has drawn the markup inside as well (search engines read it), and the component draws the same over it;
// signed in the island arrives empty. New props for an island (it
// caught up, a form it holds was answered) go into its envelope signal, and the component draws itself again.
import { createContext, render, type ComponentType } from 'preact';
import { useContext, useEffect } from 'preact/hooks';
import { signal, type Signal } from '@preact/signals';
import type { Envelope, Errors, IslandsPage } from './props.ts';

export interface IslandProps<P> {
  props: P;
  errors: Errors;
}

interface Mounted {
  name: string;
  element: HTMLElement;
  envelope: Signal<Envelope>;
  /** The island's state as it is now (a list that loaded more pages), for going back to the page. */
  snapshot?: () => unknown;
}

const registry = new Map<string, ComponentType<IslandProps<any>>>();
let mounted: Mounted[] = [];

const IslandContext = createContext<Mounted | null>(null);

export function island<P>(name: string, component: ComponentType<IslandProps<P>>): void {
  registry.set(name, component);
}

/** The component registered for an island's name. */
export function componentFor(name: string): ComponentType<IslandProps<any>> | undefined {
  return registry.get(name);
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

/** Takes over the islands under `root`; `saved` holds props to draw instead (a page gone back to, as it was left). */
export function mountIslands(root: ParentNode, saved?: ReadonlyMap<string, Envelope>): void {
  for (const element of root.querySelectorAll<HTMLElement>('[data-island]')) {
    const name = element.dataset.island ?? '';
    const component = registry.get(name);
    const envelope = saved?.get(name) ?? read(element);
    if (!component || !envelope) continue;
    const record: Mounted = { name, element, envelope: signal(envelope) };
    mounted.push(record);
    render(
      <IslandContext.Provider value={record}>
        <Host component={component} envelope={record.envelope} />
      </IslandContext.Provider>,
      element,
    );
  }
}

/** Lets the islands go (their effects end); the page they were on is about to be replaced. */
export function unmountIslands(): void {
  for (const record of mounted) render(null, record.element);
  mounted = [];
}

/** New props for the islands the page names, from an islands request. */
export function applyIslands(page: IslandsPage): void {
  for (const record of mounted) {
    const envelope = page.islands[record.name];
    if (envelope) record.envelope.value = envelope;
  }
}

/** Each island's props as it is drawn now, for drawing the page again as it was left. */
export function snapshotIslands(): Map<string, Envelope> {
  const saved = new Map<string, Envelope>();
  for (const record of mounted) {
    if (saved.has(record.name)) continue;
    const props = record.snapshot ? record.snapshot() : record.envelope.value.props;
    saved.set(record.name, { props, errors: record.envelope.value.errors });
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
