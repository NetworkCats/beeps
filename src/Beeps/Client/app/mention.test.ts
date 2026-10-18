import { test } from 'node:test';
import assert from 'node:assert/strict';
import { completeMention, mentionAt, mentionRanges } from './mention.ts';

test('a mention is found once a letter of it is typed', () => {
  assert.deepEqual(mentionAt('hi @al', 6), { start: 3, end: 6, query: 'al' });
  assert.deepEqual(mentionAt('@a', 2), { start: 0, end: 2, query: 'a' });
  assert.equal(mentionAt('hi @', 4), null);
  assert.equal(mentionAt('hi @al ', 7), null);
});

test('an agent is found by its owner and the start of its name', () => {
  assert.deepEqual(mentionAt('@tom.', 5), { start: 0, end: 5, query: 'tom.' });
  assert.deepEqual(mentionAt('@tom.cl', 7), { start: 0, end: 7, query: 'tom.cl' });
  assert.equal(mentionAt('@.tom', 5), null);
  assert.equal(mentionAt('@tom.a.b', 8), null);
});

test('an @ inside a word starts no mention, as on the server', () => {
  assert.equal(mentionAt('mail a@b', 8), null);
  assert.equal(mentionAt('你好@tom', 6), null);
  assert.equal(mentionAt('#@tom', 5), null);
  assert.deepEqual(mentionAt('你好，@tom', 7), { start: 3, end: 7, query: 'tom' });
  assert.deepEqual(mentionAt('👍@tom', 6), { start: 2, end: 6, query: 'tom' });
});

test('the handle after the caret belongs to the mention', () => {
  assert.deepEqual(mentionAt('@alice.claude hi', 3), { start: 0, end: 13, query: 'al' });
  assert.deepEqual(mentionAt('@alice. hi', 3), { start: 0, end: 6, query: 'al' });
});

test('completing replaces the whole mention and leaves the caret after a space', () => {
  const text = 'hi @al';
  assert.deepEqual(completeMention(text, mentionAt(text, 6)!, 'alice'), { text: 'hi @alice ', caret: 10 });
  const middle = '@al there';
  assert.deepEqual(completeMention(middle, mentionAt(middle, 2)!, 'alice'), { text: '@alice there', caret: 7 });
});

test('mentions are found where the server would link them', () => {
  assert.deepEqual(mentionRanges('@tom and @tom.claude, a@b @ @.x'), [{ start: 0, end: 4 }, { start: 9, end: 20 }]);
  assert.deepEqual(mentionRanges('你好@tom，@alice.'), [{ start: 7, end: 13 }]);
  assert.deepEqual(mentionRanges(`@${'a'.repeat(65)}`), []);
});
