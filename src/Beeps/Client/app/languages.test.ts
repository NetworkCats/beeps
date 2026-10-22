import { test } from 'node:test';
import assert from 'node:assert/strict';
import { languageName } from './languages.ts';

test('a language is named in the page language', () => {
  assert.equal(languageName('ja', 'en'), 'Japanese');
  assert.equal(languageName('ja', 'zh-Hans'), '日语');
  assert.equal(languageName('en', 'zh-Hans'), '英语');
});

test('a code the browser does not know stays a code', () => {
  assert.equal(languageName('qqq', 'en'), 'qqq');
  assert.equal(languageName('not a code', 'en'), 'not a code');
});
