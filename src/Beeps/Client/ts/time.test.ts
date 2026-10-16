import { test } from 'node:test';
import assert from 'node:assert/strict';
import { formatTime } from './time.ts';

// Local-time constructors keep the expectations independent of the machine's time zone.
const now = new Date(2026, 9, 16, 12, 0, 0);
const ago = (ms: number): Date => new Date(now.getTime() - ms);
const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

test('under a minute reads as now', () => {
  assert.equal(formatTime(ago(30_000), now, 'en'), 'now');
  assert.equal(formatTime(ago(30_000), now, 'zh-Hans'), '现在');
});

test('a small clock skew into the future still reads as now', () => {
  assert.equal(formatTime(ago(-5_000), now, 'en'), 'now');
});

test('minutes, hours and days are relative', () => {
  assert.equal(formatTime(ago(5 * MINUTE), now, 'en'), '5 minutes ago');
  assert.equal(formatTime(ago(5 * MINUTE), now, 'zh-Hans'), '5分钟前');
  assert.equal(formatTime(ago(3 * HOUR), now, 'en'), '3 hours ago');
  assert.equal(formatTime(ago(DAY + HOUR), now, 'en'), 'yesterday');
  assert.equal(formatTime(ago(3 * DAY), now, 'en'), '3 days ago');
});

test('a week or more shows the date, without the year when it is the current one', () => {
  assert.equal(formatTime(new Date(2026, 9, 6, 9, 0), now, 'en'), 'Oct 6');
  assert.equal(formatTime(new Date(2026, 9, 6, 9, 0), now, 'zh-Hans'), '10月6日');
  assert.equal(formatTime(new Date(2025, 9, 6, 9, 0), now, 'en'), 'Oct 6, 2025');
  assert.equal(formatTime(new Date(2025, 9, 6, 9, 0), now, 'zh-Hans'), '2025年10月6日');
});
