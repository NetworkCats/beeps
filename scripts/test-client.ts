// Runs the client tests (`*.test.ts` and `*.test.tsx` under src/Beeps/Client) with Node's test runner. Node strips the
// types of a .ts file itself but cannot run JSX, so every test is bundled by esbuild first, into node_modules/.cache.
import * as esbuild from 'esbuild';
import { spawnSync } from 'node:child_process';
import { globSync, rmSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const out = join(root, 'node_modules/.cache/client-tests');
const tests = globSync('src/Beeps/Client/**/*.test.{ts,tsx}', { cwd: root }).map((file) => join(root, file));

rmSync(out, { recursive: true, force: true });
await esbuild.build({
  entryPoints: tests,
  outdir: out,
  outbase: join(root, 'src/Beeps/Client'),
  outExtension: { '.js': '.mjs' },
  bundle: true,
  platform: 'node',
  format: 'esm',
  target: 'node24',
  jsx: 'automatic',
  jsxImportSource: 'preact',
  // Node's own modules stay imports; everything else (Preact, the code under test) is bundled.
  packages: 'bundle',
  logLevel: 'warning',
});
const outputs = globSync('**/*.test.mjs', { cwd: out }).map((file) => join(out, file));
const run = spawnSync(process.execPath, ['--test', ...outputs], { stdio: 'inherit', cwd: root });
process.exitCode = run.status ?? 1;
