// Coalesced background work: many live events in a row lead to one fetch, a fetch that failed for a reason worth
// retrying runs again later, and some fetches run at most so often.
import { retryable } from './http.ts';

// Several events in a row lead to one fetch.
const DELAY = 300;
const MAX_RETRY = 30_000;

interface State {
  timer?: number;
  running: boolean;
  again: boolean;
  started: number;
  failures: number;
}

const states = new Map<() => Promise<void>, State>();

/**
 * Runs `run` soon, once for any number of calls; calls made while it runs make it run once more. With `minInterval`,
 * not sooner than that after it last started. When it fails for a reason worth retrying, it runs again, waiting
 * longer each time.
 */
export function schedule(run: () => Promise<void>, minInterval = 0): void {
  let state = states.get(run);
  if (!state) {
    state = { running: false, again: false, started: 0, failures: 0 };
    states.set(run, state);
  }
  if (state.running) {
    state.again = true;
    return;
  }
  if (state.timer !== undefined) return;
  const current = state;
  const delay = Math.max(DELAY, current.started + minInterval - Date.now());
  current.timer = window.setTimeout(() => {
    current.timer = undefined;
    current.running = true;
    current.started = Date.now();
    let retry = 0;
    run()
      .then(
        () => {
          current.failures = 0;
        },
        (error: unknown) => {
          if (!retryable(error)) return;
          current.failures++;
          retry = Math.min(MAX_RETRY, 1000 * 2 ** (current.failures - 1));
        },
      )
      .finally(() => {
        current.running = false;
        if (current.again || retry > 0) {
          current.again = false;
          schedule(run, retry);
        }
      });
  }, delay);
}
