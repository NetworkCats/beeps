// Downloads the icon font: Material Symbols Rounded, cut down to the icons listed in
// src/Beeps/Client/icons.json, into src/Beeps/wwwroot/fonts. Node runs this file directly:
//
//   node scripts/build-icons.ts
//
// Run it after changing icons.json and commit the font; the build does not touch the network.
// One weight (300: thin strokes that sit well next to the text) and the FILL axis (outlined, and filled for
// the current page), so the file stays small.
// `npm test` checks that icons.json lists exactly the icons the pages and styles use (Client/app/icons.test.ts).
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const list = join(root, 'src/Beeps/Client/icons.json');
const out = join(root, 'src/Beeps/wwwroot/fonts/material-symbols-rounded.woff2');

const names: string[] = JSON.parse(readFileSync(list, 'utf8')).icons;
// Google Fonts wants the names sorted, and fails on duplicates.
const sorted = [...new Set(names)].sort();
const css = `https://fonts.googleapis.com/css2?family=Material+Symbols+Rounded:opsz,wght,FILL,GRAD@24,300,0..1,0&icon_names=${sorted.join(',')}&display=block`;

// A browser's user agent, or Google Fonts answers with TrueType instead of WOFF2.
const headers = { 'User-Agent': 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36' };
const response = await fetch(css, { headers });
if (!response.ok) throw new Error(`Google Fonts answered ${response.status} for ${css}`);
const url = /src:\s*url\(([^)]+)\)\s*format\('woff2'\)/.exec(await response.text())?.[1];
if (!url) throw new Error('No WOFF2 font in the Google Fonts stylesheet.');

const font = await fetch(url, { headers });
if (!font.ok) throw new Error(`Font download failed: ${font.status}`);
const bytes = Buffer.from(await font.arrayBuffer());
mkdirSync(dirname(out), { recursive: true });
writeFileSync(out, bytes);
console.log(`${sorted.length} icons, ${(bytes.length / 1024).toFixed(1)} KiB → ${out}`);
