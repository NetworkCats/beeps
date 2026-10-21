import { test } from 'node:test';
import assert from 'node:assert/strict';
import { CLOCK_MARGIN, compareIds, earlierId, liveStreamUrl, MAX_WATCHED_BEEPS, mergeIds, nextRetryDelay, NO_ID, previousId, resumesFrom } from './live-url.ts';

test('a first connection replays from when the page was rendered', () => {
  assert.equal(liveStreamUrl('/stream', 'home', ['a', 'b'], undefined, '1760603400000'), '/stream?watch=home&beeps=a%2Cb&from=1760603400000');
});

test('a reconnection replays from the last event instead', () => {
  assert.equal(liveStreamUrl('/stream', 'tag:天气', [], '1760603400000-3', '1760603400000'), '/stream?watch=tag%3A%E5%A4%A9%E6%B0%94&last_event_id=1760603400000-3');
});

test('another page is followed from the last event only when that came before the page was drawn', () => {
  const drawn = 1_700_000_010_000;
  assert.equal(resumesFrom(`${drawn - CLOCK_MARGIN - 1}-0`, drawn), true);
  // Events about this page may have come just before it, which the connection for the last page passed over.
  assert.equal(resumesFrom(`${drawn - CLOCK_MARGIN}-0`, drawn), false);
  assert.equal(resumesFrom(`${drawn + 500}-3`, drawn), false);
});

test('with no last event, or no time the page was drawn, the connection starts as it would anyway', () => {
  assert.equal(resumesFrom(undefined, 1_700_000_010_000), true);
  assert.equal(resumesFrom('1700000010000-0', Number.NaN), true);
});

test('a page without a topic or beeps still connects, for its inbox count', () => {
  assert.equal(liveStreamUrl('/stream', '', [], undefined, undefined), '/stream');
});

test('a page with more beeps than one connection lists asks for everything about beeps', () => {
  const many = Array.from({ length: MAX_WATCHED_BEEPS + 1 }, (_, i) => `id${i}`);
  const url = new URL(liveStreamUrl('/stream', 'home', many, undefined, undefined), 'http://x');
  assert.equal(url.searchParams.get('all'), 'true');
  assert.equal(url.searchParams.get('beeps')?.split(',').length, MAX_WATCHED_BEEPS);
  assert.equal(new URL(liveStreamUrl('/stream', 'home', ['a', 'a'], undefined, undefined), 'http://x').searchParams.get('all'), null);
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

test('the id before another borrows across digits and keeps the dashes', () => {
  assert.equal(previousId('0199f0a2-3b4c-7d5e-8f60-000000000001'), '0199f0a2-3b4c-7d5e-8f60-000000000000');
  assert.equal(previousId('0199f0a2-3b4c-7d5e-8f61-000000000000'), '0199f0a2-3b4c-7d5e-8f60-ffffffffffff');
  assert.equal(previousId('0199F0A2-3B4C-7D5E-8F60-0000000000A0'), '0199f0a2-3b4c-7d5e-8f60-00000000009f');
});

test('nothing comes before the empty id, nor before something that is not an id', () => {
  assert.equal(previousId(NO_ID), NO_ID);
  assert.equal(previousId('nope'), NO_ID);
});

test('ids order as their text, newest last', () => {
  assert.ok(compareIds('0199f0a2-0000-7000-8000-000000000000', '0199f0a3-0000-7000-8000-000000000000') < 0);
  assert.ok(compareIds('0199F0A3-0000-7000-8000-000000000000', '0199f0a2-ffff-7000-8000-000000000000') > 0);
  assert.equal(compareIds('a', 'A'), 0);
});

test('asking from a little earlier also brings a beep committed after a newer one', () => {
  const newest = '019a0000-1000-7abc-8def-0123456789ab';
  const late = '019a0000-0c00-7fff-bfff-ffffffffffff';
  const earlier = earlierId(newest, 3000);
  assert.equal(earlier, '019a0000-0448-7000-8000-000000000000');
  assert.ok(compareIds(late, earlier) > 0);
  assert.ok(compareIds(earlier, newest) < 0);
  assert.equal(earlierId(NO_ID, 3000), NO_ID);
});
