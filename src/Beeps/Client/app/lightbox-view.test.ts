import { test } from 'node:test';
import assert from 'node:assert/strict';
import { clampView, fromThumbnail, IDENTITY, MAX_SCALE, zoomAt } from './lightbox-view.ts';

test('zooming keeps the point under the finger where it is', () => {
  // A picture centred at (500, 400); zoomed twice about (600, 400), the point 100px right of the centre stays put.
  const view = zoomAt(IDENTITY, 2, 600, 400, 500, 400);
  assert.deepEqual(view, { scale: 2, x: -100, y: 0 });
  // Where that point now is: centre + offset + 100px × scale.
  assert.equal(500 + view.x + 100 * view.scale, 600);
});

test('zooming back about the same point undoes it', () => {
  const zoomed = zoomAt(IDENTITY, 3, 120, 640, 500, 400);
  assert.deepEqual(zoomAt(zoomed, 1, 120, 640, 500, 400), { scale: 1, x: 0, y: 0 });
});

test('an unzoomed picture cannot be moved', () => {
  assert.deepEqual(clampView({ scale: 1, x: 40, y: -30 }, 800, 600, 1000, 800), IDENTITY);
});

test('a zoomed picture moves only until its edge reaches the screen', () => {
  // 800 × 600 at twice its size is 1600 × 1200 on a 1000 × 800 screen: 300px of play sideways, 200px up and down.
  assert.deepEqual(clampView({ scale: 2, x: 500, y: -500 }, 800, 600, 1000, 800), { scale: 2, x: 300, y: -200 });
});

test('the zoom stays between fitting and the largest', () => {
  assert.equal(clampView({ scale: 0.5, x: 0, y: 0 }, 800, 600, 1000, 800).scale, 1);
  assert.equal(clampView({ scale: 9, x: 0, y: 0 }, 800, 600, 1000, 800).scale, MAX_SCALE);
});

test('a picture grows out of a thumbnail of the same shape without being cut', () => {
  const { transform, clipPath } = fromThumbnail({ left: 0, top: 0, width: 100, height: 50 }, { left: 100, top: 200, width: 400, height: 200 });
  assert.equal(transform, 'translate(-250px, -275px) scale(0.25)');
  assert.equal(clipPath, 'inset(0px 0px)');
});

test('a thumbnail that crops the picture is matched by cutting the picture the same way', () => {
  // A square thumbnail of a 2:1 picture shows its middle half.
  const { transform, clipPath } = fromThumbnail({ left: 0, top: 0, width: 100, height: 100 }, { left: 0, top: 0, width: 400, height: 200 });
  assert.equal(transform, 'translate(-150px, -50px) scale(0.5)');
  assert.equal(clipPath, 'inset(0px 100px)');
});
