// Removals counted as they come (beeps.ts), and the count each page's data was fetched at: data fetched before a beep
// went must not bring it back, whichever island draws it and whenever (fetched as it was hidden by review, say).

let removals = 0;
const fetchedAt = new WeakMap<object, number>();

/** Counts one more removal; the count it went at. */
export function countRemoval(): number {
  return ++removals;
}

/** Taken as a fetch starts, and given back to `putBeeps` with what it brought. */
export function removalMark(): number {
  return removals;
}

/** Marks every object of a fetched answer with the removal count its fetch started at. */
export function markFetched<T>(answer: T, since: number): T {
  const pending: unknown[] = [answer];
  while (pending.length > 0) {
    const value = pending.pop();
    if (typeof value !== 'object' || value === null || fetchedAt.has(value)) continue;
    fetchedAt.set(value, since);
    pending.push(...Object.values(value));
  }
  return answer;
}

/** The removal count the answer holding `value` was fetched at, if it came from one. */
export function fetchedMark(value: object): number | undefined {
  return fetchedAt.get(value);
}
