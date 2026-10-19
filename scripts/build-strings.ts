// Lists the resource keys the app's components show (Client/strings.json): the server sends just those to the browser,
// in the page's language (Pages/Islands/ClientStrings.cs). A key is any string literal in Client/app that looks like one
// (`'Beep.Delete'`), so keys are always written out in full, never put together.
//
//   node scripts/build-strings.ts           writes the list
//   node scripts/build-strings.ts --check   fails when it is out of date (npm test)
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { appSources, client } from './client-sources.ts';

const target = join(client, 'strings.json');

function keysIn(source: string): string[] {
  return [...source.matchAll(/'([A-Z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+)'/g)].map((match) => match[1] ?? '');
}

const keys = new Set<string>();
for (const path of appSources()) {
  for (const key of keysIn(readFileSync(path, 'utf8'))) keys.add(key);
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
