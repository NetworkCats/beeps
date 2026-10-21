// Interface text, from the resource files (SharedResource.*.resx): the keys the app uses are listed in
// Client/strings.json, and the server sends them in the page's language as a JSON document of their own
// (Pages/Islands/ClientStrings.cs), not in every page. The layout's config names its address, which changes with the
// language and whenever the text does; the document is kept in the browser under that address, so it is fetched once.
// `{0}`, `{1}`… are filled in as .NET's string.Format would.
import { config } from './config.ts';

const STORAGE_KEY = 'beeps:strings';
// Which text an answer holds (ClientStringsEndpoints.VersionHeader).
const VERSION_HEADER = 'Beeps-Strings-Version';

interface Stored {
  url: string;
  strings: Record<string, string>;
}

let current: Stored = { url: '', strings: {} };
let loading: { url: string; done: Promise<void> } | undefined;

function format(text: string, args: readonly unknown[]): string {
  return text.replace(/\{(\d+)\}/g, (match, index: string) => {
    const value = args[Number(index)];
    return value === undefined ? match : String(value);
  });
}

export function t(key: string, ...args: unknown[]): string {
  const text = current.strings[key];
  return text === undefined ? key : format(text, args);
}

export function setStrings(url: string, strings: Record<string, string>): void {
  current = { url, strings };
}

function stored(url: string): Stored | undefined {
  try {
    const saved = JSON.parse(window.localStorage.getItem(STORAGE_KEY) ?? 'null') as Stored | null;
    return saved?.url === url ? saved : undefined;
  } catch {
    return undefined;
  }
}

function store(saved: Stored): void {
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(saved));
  } catch {
    // Storage full or turned off: fetched again next time (from the browser's cache, most likely).
  }
}

async function fetchStrings(url: string): Promise<void> {
  const response = await fetch(url, { credentials: 'same-origin', priority: 'high' });
  if (!response.ok) throw new Error(String(response.status));
  const strings = (await response.json()) as Record<string, string>;
  // During a deployment an instance still on the old text answers for the new address with what it has: shown for
  // now, but kept under no address, so the next page asks again rather than keep the old text for good.
  if (response.headers.get(VERSION_HEADER) !== new URL(url, window.location.href).searchParams.get('v')) {
    current = { url: '', strings };
    return;
  }
  const saved: Stored = { url, strings };
  current = saved;
  store(saved);
}

/**
 * The text in the language the config names, before anything is drawn with it: at once when it is the one in use or
 * kept in the browser, otherwise fetched first. Fails when it cannot be fetched, as a page's code would.
 */
export function loadStrings(): Promise<void> {
  const url = config().stringsUrl;
  if (!url || current.url === url) return Promise.resolve();
  const saved = stored(url);
  if (saved) {
    current = saved;
    return Promise.resolve();
  }
  if (loading?.url !== url) {
    const done = fetchStrings(url);
    loading = { url, done };
    // Offline for a moment: the next page tries again.
    done.catch(() => {
      if (loading?.done === done) loading = undefined;
    });
  }
  return loading.done;
}
