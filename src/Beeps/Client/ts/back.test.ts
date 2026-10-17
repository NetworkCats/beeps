import { test } from 'node:test';
import assert from 'node:assert/strict';
import { returnsToReferrer } from './back.ts';

const here = 'https://beeps.test/beeps/b1';

test('a page opened from another page of the site goes back to it', () => {
  assert.equal(returnsToReferrer('https://beeps.test/@ada', here, null), true);
  assert.equal(returnsToReferrer('https://beeps.test/tags/天气?before=x', here, null), true);
});

test('a page opened directly or from another site uses its link', () => {
  assert.equal(returnsToReferrer('', here, null), false);
  assert.equal(returnsToReferrer('https://elsewhere.test/@ada', here, null), false);
});

test('a page reached from itself uses its link', () => {
  assert.equal(returnsToReferrer('https://beeps.test/beeps/b1#reply', here, null), false);
});

test('a form just sent is not gone back to', () => {
  assert.equal(returnsToReferrer('https://beeps.test/beeps/b1/report', here, 'https://beeps.test/beeps/b1/report'), false);
  assert.equal(returnsToReferrer('https://beeps.test/beeps/b1/report', here, 'https://beeps.test/compose'), true);
});
