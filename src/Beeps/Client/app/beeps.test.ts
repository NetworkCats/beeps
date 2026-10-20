import { test } from 'node:test';
import assert from 'node:assert/strict';
import { beepSignal, putBeeps, removalMark, removeAuthors, removeBeep } from './beeps.ts';
import type { BeepProps } from './props.ts';
import { markFetched } from './removals.ts';

function beep(id: string, author = 'author'): BeepProps {
  return { id, author: { id: author } as BeepProps['author'], createdAt: '', html: '', media: [], likeCount: 0, replyCount: 0, mentions: [] };
}

test('an answer fetched before a beep went does not bring it back', () => {
  putBeeps([beep('gone')]);
  const since = removalMark();
  removeBeep('gone');
  putBeeps([beep('gone'), beep('kept')], since);
  assert.equal(beepSignal('gone').value, null);
  assert.equal(beepSignal('kept').value?.id, 'kept');
});

test('one fetched after it went shows it again (shown again after review)', () => {
  removeBeep('back');
  const since = removalMark();
  putBeeps([beep('back')], since);
  assert.equal(beepSignal('back').value?.id, 'back');
});

test('an answer fetched before its author went does not bring their beeps back', () => {
  putBeeps([beep('theirs', 'banned')]);
  const since = removalMark();
  removeAuthors(['banned']);
  putBeeps([beep('theirs', 'banned')], since);
  assert.equal(beepSignal('theirs').value, null);
});

test("a page's data fetched before a beep went does not bring it back, without a mark given", () => {
  putBeeps([beep('hidden')]);
  const since = removalMark();
  const answer = markFetched({ islands: { Feed: { props: { items: [beep('hidden'), beep('shown')] } } } }, since);
  removeBeep('hidden');
  putBeeps(answer.islands.Feed.props.items);
  assert.equal(beepSignal('hidden').value, null);
  assert.equal(beepSignal('shown').value?.id, 'shown');
});
