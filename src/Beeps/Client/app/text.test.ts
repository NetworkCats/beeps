import { test } from 'node:test';
import assert from 'node:assert/strict';
import { checkImages, countCharacters, isImageType } from './text.ts';

test('a Chinese character or an emoji counts as one, like on the server', () => {
  assert.equal(countCharacters('你好'), 2);
  assert.equal(countCharacters('👍🏽'), 1);
  assert.equal(countCharacters('👨‍👩‍👧'), 1);
});

test('line breaks are unified and surrounding whitespace is not counted', () => {
  assert.equal(countCharacters('  a\r\nb  '), 3);
  assert.equal(countCharacters('\n\n'), 0);
});
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

test('only the image types the site takes are taken from a drop or a paste', () => {
  assert.equal(isImageType('image/png'), true);
  assert.equal(isImageType('IMAGE/JPEG'), true);
  assert.equal(isImageType('image/heic'), false);
  assert.equal(isImageType('application/pdf'), false);
  assert.equal(isImageType(''), false);
});
