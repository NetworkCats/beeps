import { test } from 'node:test';
import assert from 'node:assert/strict';
import { itemsToAdd, itemsToKeep } from './more-items.ts';

test('a fetched page adds what the list does not show yet', () => {
  assert.deepEqual(itemsToAdd(new Set(['a', 'b']), ['b', 'c', 'd']), [1, 2]);
});

test('what goes with an item shown already is left out with it', () => {
  // A thread's focused beep shown above its page, then fetched with its reply box.
  assert.deepEqual(itemsToAdd(new Set(['f']), ['e', 'f', undefined, 'g']), [0, 3]);
});

test('the items past the fresh first page stay after it', () => {
  // Two new items pushed c and d off the first page: they stay, before what was loaded below.
  assert.deepEqual(itemsToKeep(['a', 'b', 'c', 'd', 'e', 'f'], ['y', 'z', 'a', 'b']), [2, 3, 4, 5]);
});

test('an item gone from the first page lets the next one move up without showing it twice', () => {
  assert.deepEqual(itemsToKeep(['a', 'b', 'c', 'd', 'e'], ['a', 'c', 'd']), [4]);
});

test('a first page of new items only keeps everything below it', () => {
  assert.deepEqual(itemsToKeep(['a', 'b', 'c'], ['x', 'y']), [0, 1, 2]);
});

test('an item the fresh page has elsewhere is not kept twice', () => {
  assert.deepEqual(itemsToKeep(['a', 'b', 'c'], ['x', 'c']), []);
  assert.deepEqual(itemsToKeep(['a', 'b', 'c', 'd'], ['d', 'a']), [1, 2]);
});

test('what goes with a kept item stays with it', () => {
  assert.deepEqual(itemsToKeep(['a', 'b', undefined, 'c'], ['a']), [1, 2, 3]);
});
