// Requests the app makes of the site: islands requests (a page's props as JSON, Pages/Islands/IslandRequests.cs), the
// /live fetches, and forms sent in the background.
import { config } from './config.ts';
import type { IslandsPage } from './props.ts';

export const ISLANDS_HEADER = 'X-Beeps-Islands';

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

/** What an islands request came back with: where it ended up (after redirects), and the page's props when it has islands. */
export interface IslandsAnswer {
  url: string;
  status: number;
  page: IslandsPage | null;
}

async function islandsAnswer(response: Response): Promise<IslandsAnswer> {
  const json = response.headers.get('Content-Type')?.includes('application/json');
  return { url: response.url, status: response.status, page: json ? ((await response.json()) as IslandsPage) : null };
}

/** The props of the page at `url`, for this viewer, as a reload would draw them. */
export async function getIslands(url: string): Promise<IslandsAnswer> {
  const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', headers: { [ISLANDS_HEADER]: '1' } });
  if (response.status >= 500 || response.status === 429) throw new HttpError(response.status);
  return islandsAnswer(response);
}

/**
 * Sends a form the way the browser would, and gets the props of the page it leads to (after its redirect), or of the
 * form's own page drawn again with what was wrong.
 */
export async function postIslands(action: string, body: FormData): Promise<IslandsAnswer> {
  const response = await fetch(action, { method: 'POST', body, credentials: 'same-origin', headers: { [ISLANDS_HEADER]: '1' } });
  return islandsAnswer(response);
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
