// The parts of live.ts that need no browser, so Node can test them.

/** Matches LiveService.MaxWatchedBeeps on the server. */
export const MAX_WATCHED_BEEPS = 120;

const MAX_RETRY_DELAY = 30_000;

/** Distinct non-empty ids in page order, at most as many as the server accepts. */
export function mergeIds(ids: readonly string[]): string[] {
  return [...new Set(ids.filter((id) => id.length > 0))].slice(0, MAX_WATCHED_BEEPS);
}

/**
 * The stream URL for the page. A reconnect names the last event it got; a first connection names when the
 * page was rendered, so whatever happened in between is replayed.
 */
export function liveStreamUrl(base: string, watch: string, beeps: readonly string[], lastEventId: string | undefined, renderedAt: string | undefined): string {
  const params = new URLSearchParams();
  if (watch) params.set('watch', watch);
  if (beeps.length > 0) params.set('beeps', beeps.join(','));
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
