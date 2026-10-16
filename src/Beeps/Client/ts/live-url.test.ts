import { test } from 'node:test';
import assert from 'node:assert/strict';
import { liveStreamUrl, MAX_WATCHED_BEEPS, mergeIds, nextRetryDelay } from './live-url.ts';

test('a first connection replays from when the page was rendered', () => {
  assert.equal(liveStreamUrl('/stream', 'home', ['a', 'b'], undefined, '1760603400000'), '/stream?watch=home&beeps=a%2Cb&from=1760603400000');
});

test('a reconnection replays from the last event instead', () => {
  assert.equal(liveStreamUrl('/stream', 'tag:天气', [], '1760603400000-3', '1760603400000'), '/stream?watch=tag%3A%E5%A4%A9%E6%B0%94&last_event_id=1760603400000-3');
});

test('a page without a topic or beeps still connects, for its inbox count', () => {
  assert.equal(liveStreamUrl('/stream', '', [], undefined, undefined), '/stream');
});

test('ids are distinct, non-empty and capped', () => {
  assert.deepEqual(mergeIds(['a', '', 'b', 'a']), ['a', 'b']);
  assert.equal(mergeIds(Array.from({ length: 500 }, (_, i) => String(i))).length, MAX_WATCHED_BEEPS);
});

test('retries back off to 30 seconds', () => {
  assert.equal(nextRetryDelay(0, () => 0), 1000);
  assert.equal(nextRetryDelay(1000, () => 0), 2000);
  assert.equal(nextRetryDelay(20_000, () => 0), 30_000);
  assert.equal(nextRetryDelay(30_000, () => 1), 36_000);
});
