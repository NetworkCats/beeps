// The parts of the live connection (live.ts) and of the lists that need no browser, so Node can test them.

/** Matches LiveService.MaxWatchedBeeps on the server. */
export const MAX_WATCHED_BEEPS = 120;

const MAX_RETRY_DELAY = 30_000;

/** Distinct non-empty ids in page order, at most as many as the server accepts. */
export function mergeIds(ids: readonly string[]): string[] {
  return [...new Set(ids.filter((id) => id.length > 0))].slice(0, MAX_WATCHED_BEEPS);
}

/**
 * The stream URL for the page, for the beeps it shows (any number). A reconnect names the last event it got; a first
 * connection names when the page was rendered, so whatever happened in between is replayed.
 */
export function liveStreamUrl(base: string, watch: string, beeps: readonly string[], lastEventId: string | undefined, renderedAt: string | undefined): string {
  const params = new URLSearchParams();
  if (watch) params.set('watch', watch);
  const listed = mergeIds(beeps);
  if (listed.length > 0) params.set('beeps', listed.join(','));
  // More than one connection may list: the server then sends what concerns any beep, and the page keeps its own.
  if (new Set(beeps.filter((id) => id.length > 0)).size > listed.length) params.set('all', 'true');
  if (lastEventId) params.set('last_event_id', lastEventId);
  else if (renderedAt) params.set('from', renderedAt);
  const query = params.toString();
  return query ? `${base}?${query}` : base;
}

/** 1 s, then doubling to 30 s, with up to a fifth added at random so tabs do not reconnect in step. */
export function nextRetryDelay(previous: number, random: () => number = Math.random): number {
  const base = previous <= 0 ? 1000 : Math.min(previous * 2, MAX_RETRY_DELAY);
  return Math.round(base * (1 + random() * 0.2));
}

export const NO_ID = '00000000-0000-0000-0000-000000000000';

/**
 * The id just before `id`, for asking "from this one on" where the server takes "after this one". Beep and
 * notification ids are UUIDv7 and the server orders them as their text orders, so plain hex arithmetic works.
 */
export function previousId(id: string): string {
  const digits = id.replace(/-/g, '').toLowerCase().split('');
  if (digits.length !== 32 || digits.every((d) => d === '0')) return NO_ID;
  for (let i = digits.length - 1; i >= 0; i--) {
    const digit = parseInt(digits[i] ?? '0', 16);
    if (digit > 0) {
      digits[i] = (digit - 1).toString(16);
      break;
    }
    digits[i] = 'f';
  }
  const hex = digits.join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

/**
 * The first id made `ms` before this one (UUIDv7: the time comes first). Ids are made before the insert, and two
 * inserts can commit in either order: asking for what came after this one instead also brings one committed late.
 */
export function earlierId(id: string, ms: number): string {
  const hex = id.replace(/-/g, '').toLowerCase();
  if (hex.length !== 32 || id === NO_ID) return id;
  const time = Math.max(0, parseInt(hex.slice(0, 12), 16) - ms).toString(16).padStart(12, '0');
  return `${time.slice(0, 8)}-${time.slice(8, 12)}-7000-8000-000000000000`;
}

/** Orders ids as the server does: newest last. */
export function compareIds(a: string, b: string): number {
  const x = a.toLowerCase();
  const y = b.toLowerCase();
  return x < y ? -1 : x > y ? 1 : 0;
}
