import { test } from 'node:test';
import assert from 'node:assert/strict';
import { countCharacters } from './compose.ts';

test('a Chinese character or an emoji counts as one, like on the server', () => {
  assert.equal(countCharacters('你好'), 2);
  assert.equal(countCharacters('👍🏽'), 1);
  assert.equal(countCharacters('👨‍👩‍👧'), 1);
});

test('line breaks are unified and surrounding whitespace is not counted', () => {
  assert.equal(countCharacters('  a\r\nb  '), 3);
  assert.equal(countCharacters('\n\n'), 0);
});
