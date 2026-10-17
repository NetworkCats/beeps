import { test } from 'node:test';
import assert from 'node:assert/strict';
import { toCropFields } from './avatar-crop-fields.ts';

test('the selection becomes whole pixels of the original image', () => {
  assert.deepEqual(toCropFields({ x: 10.4, y: 20.6, width: 300.2, height: 300.2 }), { x: 10, y: 21, size: 300 });
});

test('a rounding pixel between the sides gives the smaller square', () => {
  assert.deepEqual(toCropFields({ x: 0, y: 0, width: 401, height: 400 }), { x: 0, y: 0, size: 400 });
});

test('a selection a hair outside the image starts at its edge', () => {
  assert.deepEqual(toCropFields({ x: -0.6, y: -0.2, width: 50, height: 50 }), { x: 0, y: 0, size: 50 });
});
