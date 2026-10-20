// Requests the app makes of the site: a page's data (JSON from /page-data, Pages/Islands/PageDataMiddleware.cs), the
// /live fetches, and forms sent in the background.
import { config } from './config.ts';
import type { IslandsPage } from './props.ts';
import { markFetched, removalMark } from './removals.ts';

const PAGE_DATA = '/page-data';

/** Where the data of the page at `url` is: the page's address under /page-data. */
export function pageDataUrl(url: string): string {
  const target = new URL(url, window.location.href);
  return `${target.origin}${PAGE_DATA}${target.pathname}${target.search}`;
}

/** The page whose data is at `url` (a redirect stays under /page-data); any other address as it is. */
export function pageOf(url: string): string {
  const target = new URL(url, window.location.href);
  if (target.pathname === PAGE_DATA || target.pathname.startsWith(`${PAGE_DATA}/`)) {
    target.pathname = target.pathname.slice(PAGE_DATA.length) || '/';
  }
  return target.href;
}

/** A fetch the server answered with an error. */
export class HttpError extends Error {
  readonly status: number;

  constructor(status: number) {
    super(String(status));
    this.status = status;
  }
}

/**
 * Worth trying again: the network or the server failed (an instance going away, say), not the request. What asked
 * for the fetch (a live event) has been counted as seen, so giving up would lose what it announced for good.
 */
export function retryable(error: unknown): boolean {
  return error instanceof TypeError || (error instanceof HttpError && (error.status >= 500 || error.status === 429));
}

/** What a page's data came back as: the page it ended up on (after redirects), and its data when it is a page. */
export interface IslandsAnswer {
  url: string;
  status: number;
  /** A redirect was followed: a form sent got as far as being done. */
  redirected: boolean;
  page: IslandsPage | null;
}

async function islandsAnswer(response: Response, since: number): Promise<IslandsAnswer> {
  const json = response.headers.get('Content-Type')?.includes('application/json');
  const page = json ? markFetched((await response.json()) as IslandsPage, since) : null;
  return { url: pageOf(response.url), status: response.status, redirected: response.redirected, page };
}

/** The data of the page at `url`, for this viewer, as a reload would draw it. */
export async function getIslands(url: string): Promise<IslandsAnswer> {
  const since = removalMark();
  const response = await fetch(pageDataUrl(url), { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
  if (response.status >= 500 || response.status === 429) throw new HttpError(response.status);
  return islandsAnswer(response, since);
}

/**
 * Sends a form the way the browser would, and gets the data of the page it leads to (after its redirect), or of the
 * form's own page drawn again with what was wrong.
 */
export async function postIslands(action: string, body: FormData): Promise<IslandsAnswer> {
  const since = removalMark();
  const response = await fetch(pageDataUrl(action), { method: 'POST', body, credentials: 'same-origin', headers: { Accept: 'application/json' } });
  return islandsAnswer(response, since);
}

export async function getJson<T>(url: string): Promise<T> {
  const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
  if (!response.ok) throw new HttpError(response.status);
  return (await response.json()) as T;
}

/** The form's fields, with the antiforgery token a form drawn here carries too. */
export function formData(fields: Record<string, string> = {}): FormData {
  const data = new FormData();
  const { field, token } = config().antiforgery;
  if (field) data.set(field, token);
  for (const [name, value] of Object.entries(fields)) data.set(name, value);
  return data;
}
