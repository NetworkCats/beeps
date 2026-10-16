import { test } from 'node:test';
import assert from 'node:assert/strict';
import { checkImages } from './images.ts';

const mb = 1024 * 1024;

test('up to the limits is fine', () => {
  assert.equal(checkImages([], 4, 5 * mb), null);
  assert.equal(checkImages([mb, mb, mb, 5 * mb], 4, 5 * mb), null);
});

test('too many files are flagged before their sizes', () => {
  assert.equal(checkImages([1, 1, 1, 1, 6 * mb], 4, 5 * mb), 'too-many');
});

test('one file over the size limit is enough', () => {
  assert.equal(checkImages([mb, 5 * mb + 1], 4, 5 * mb), 'too-large');
});
