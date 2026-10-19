// The app's own sources, which the build scripts read for what they register: lazy islands (build-client.ts), resource
// keys (build-strings.ts). Its tests are left out: what they name is not shipped.
import { globSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
export const client = join(root, 'src/Beeps/Client');

const test = /\.test\.tsx?$/;

/** Every .ts and .tsx file under Client/app but the tests, as absolute paths in a stable order. */
export function appSources(): string[] {
  return globSync('app/**/*.{ts,tsx}', { cwd: client })
    .filter((file) => !test.test(file))
    .sort()
    .map((file) => join(client, file));
}

/** Whether a changed file (relative to Client, as fs.watch names it) is one of appSources(). */
export function isAppSource(file: string): boolean {
  return /^app[\\/].*\.tsx?$/.test(file) && !test.test(file);
}
