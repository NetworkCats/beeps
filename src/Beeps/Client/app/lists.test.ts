import { test } from 'node:test';
import assert from 'node:assert/strict';
import { appendPage, holdNewer, insertOrdered, mergeFresh } from './lists.ts';

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

test('a first page of new beeps only is the list as it is, with no gap above what was shown', () => {
  assert.deepEqual(mergeFresh({ ids: ['a', 'b', 'c'], next: '?before=c' }, { ids: ['x', 'y'], next: '?before=y' }), { ids: ['x', 'y'], next: '?before=y' });
});

test('a fresh page that ends among the beeps waiting above the list keeps what was shown below them', () => {
  // w1 and w2 wait behind "Show new beeps" above a, b, c (BeepFeed counts them as shown); the viewer posted o.
  assert.deepEqual(mergeFresh({ ids: ['w1', 'w2', 'a', 'b', 'c'], next: '?before=c' }, { ids: ['o', 'w1', 'w2'], next: '?before=w2' }), {
    ids: ['o', 'w1', 'w2', 'a', 'b', 'c'],
    next: '?before=c',
  });
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

test('new beeps above the top of a timeline wait to be shown; the viewer\'s own, and ones that came back, do not', () => {
  const id = (n: number): string => `01900000-0000-7000-8000-00000000000${n}`;
  // 1 to 3 shown, 3 at the top; 5 and 6 are new, 6 the viewer's own; 2 came back below the top.
  const had = new Set([id(1), id(3)]);
  const result = holdNewer([id(6), id(5), id(3), id(2), id(1)], had, id(3), (beep) => beep === id(6));
  assert.deepEqual(result, { ids: [id(6), id(3), id(2), id(1)], held: [id(5)] });
});

test('an empty timeline shows its new beeps at once', () => {
  assert.deepEqual(holdNewer(['b', 'a'], new Set(), undefined, () => false), { ids: ['b', 'a'], held: [] });
});
