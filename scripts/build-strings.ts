// Lists the resource keys the app's components show (Client/strings.json): the server sends just those to the browser,
// in the page's language (Pages/Islands/ClientStrings.cs). A key is any string literal in Client/app that looks like one
// (`'Beep.Delete'`), so keys are always written out in full, never put together.
//
//   node scripts/build-strings.ts           writes the list
//   node scripts/build-strings.ts --check   fails when it is out of date (npm test)
import { readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const app = join(root, 'src/Beeps/Client/app');
const target = join(root, 'src/Beeps/Client/strings.json');

export function keysIn(source: string): string[] {
  return [...source.matchAll(/'([A-Z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+)'/g)].map((match) => match[1] ?? '');
}

const keys = new Set<string>();
for (const name of readdirSync(app, { recursive: true, encoding: 'utf8' })) {
  if (!/\.tsx?$/.test(name) || name.includes('.test.')) continue;
  for (const key of keysIn(readFileSync(join(app, name), 'utf8'))) keys.add(key);
}
const json = `${JSON.stringify([...keys].sort(), null, 2)}\n`;

if (process.argv.includes('--check')) {
  if (readFileSync(target, 'utf8') !== json) {
    console.error('Client/strings.json is out of date: run `npm run strings`.');
    process.exitCode = 1;
  }
} else {
  writeFileSync(target, json);
  console.log(`[strings] ${keys.size} keys`);
}
