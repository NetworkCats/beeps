import { test } from 'node:test';
import assert from 'node:assert/strict';
import { appendPage, insertOrdered, mergeFresh } from './lists.ts';

test('the beeps past the fresh first page stay after it, with the next page from where they end', () => {
  // Two new beeps pushed c and d off the first page: they stay, before what was loaded below.
  assert.deepEqual(mergeFresh({ ids: ['a', 'b', 'c', 'd', 'e', 'f'], next: '?before=f' }, { ids: ['y', 'z', 'a', 'b'], next: '?before=b' }), {
    ids: ['y', 'z', 'a', 'b', 'c', 'd', 'e', 'f'],
    next: '?before=f',
  });
});

test('a beep gone from the first page lets the next one move up without showing it twice', () => {
  assert.deepEqual(mergeFresh({ ids: ['a', 'b', 'c', 'd', 'e'] }, { ids: ['a', 'c', 'd'] }).ids, ['a', 'c', 'd', 'e']);
});

test('a first page of new beeps only keeps everything below it', () => {
  assert.deepEqual(mergeFresh({ ids: ['a', 'b', 'c'] }, { ids: ['x', 'y'] }).ids, ['x', 'y', 'a', 'b', 'c']);
});

test('a beep the fresh page has elsewhere is not kept twice', () => {
  assert.deepEqual(mergeFresh({ ids: ['a', 'b', 'c'] }, { ids: ['x', 'c'] }).ids, ['x', 'c']);
  assert.deepEqual(mergeFresh({ ids: ['a', 'b', 'c', 'd'] }, { ids: ['d', 'a'] }).ids, ['d', 'a', 'b', 'c']);
});

test('a fresh page with nothing on it empties the list', () => {
  assert.deepEqual(mergeFresh({ ids: ['a', 'b'], next: '?before=b' }, { ids: [] }), { ids: [], next: undefined });
});

test('nothing kept past the fresh page: its own next page goes on from it', () => {
  assert.deepEqual(mergeFresh({ ids: ['a', 'b'], next: '?before=b' }, { ids: ['a', 'b'], next: '?before=x' }), { ids: ['a', 'b'], next: '?before=x' });
});

test('a page loaded at the end adds only what the list does not show yet', () => {
  assert.deepEqual(appendPage({ ids: ['a', 'b'], next: '1' }, { ids: ['b', 'c', 'd'], next: '2' }), { ids: ['a', 'b', 'c', 'd'], next: '2' });
});

test('new beeps go where they belong: newest first in a timeline', () => {
  const a = '01900000-0000-7000-8000-000000000001';
  const b = '01900000-0000-7000-8000-000000000002';
  const c = '01900000-0000-7000-8000-000000000003';
  assert.deepEqual(insertOrdered([b], [c, a, b], false), [c, b, a]);
});

test('new replies go where they belong: oldest first in a thread', () => {
  const a = '01900000-0000-7000-8000-000000000001';
  const b = '01900000-0000-7000-8000-000000000002';
  const c = '01900000-0000-7000-8000-000000000003';
  assert.deepEqual(insertOrdered([b], [c, a], true), [a, b, c]);
});
