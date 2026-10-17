// The icon font holds only the icons in Client/icons.json (scripts/build-icons.ts). These checks keep that list
// exactly what the site draws: a name missing from it would show as its ligature text ("home"), and a name nobody
// uses any more is dead weight in the font.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const client = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const pages = resolve(client, '../Pages');
const listed: string[] = JSON.parse(readFileSync(join(client, 'icons.json'), 'utf8')).icons;

function files(dir: string, extension: string): string[] {
  return readdirSync(dir, { recursive: true, encoding: 'utf8' })
    .filter((name) => name.endsWith(extension))
    .map((name) => join(dir, name));
}

function names(paths: string[], pattern: RegExp): Set<string> {
  const found = new Set<string>();
  for (const path of paths) for (const [, name] of readFileSync(path, 'utf8').matchAll(pattern)) if (name) found.add(name);
  return found;
}

// <icon name="…" /> in the pages, and glyphs the styles draw themselves (the stepper's tick, the close button).
const inPages = names(files(pages, '.cshtml'), /<icon name="([^"]+)"/g);
const inStyles = names(files(client, '.scss'), /icon-glyph\("([^"]+)"\)/g);

test('every icon a page uses is in the font', () => {
  assert.deepEqual([...inPages].filter((name) => !listed.includes(name)), []);
});

test('the font holds no icon that nothing uses', () => {
  assert.deepEqual(listed.filter((name) => !inPages.has(name) && !inStyles.has(name)), []);
});

test('the list is sorted and has no duplicates', () => {
  assert.deepEqual(listed, [...new Set(listed)].sort());
});
