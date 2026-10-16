// Builds the client assets into src/Beeps/wwwroot/dist. Node runs this file directly.
//
//   node scripts/build-client.ts           one-shot production build (minified, no source maps)
//   node scripts/build-client.ts --watch   rebuild on change, with source maps
//
// File names stay stable (main.js, main.css): MapStaticAssets fingerprints them at `dotnet build`,
// so cache busting is not this script's job.
import * as esbuild from 'esbuild';
import * as sass from 'sass';
import { mkdirSync, rmSync, watch, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const client = join(root, 'src/Beeps/Client');
const out = join(root, 'src/Beeps/wwwroot/dist');
const watchMode = process.argv.includes('--watch');

// Site-wide `main` plus page-specific entries (onboarding, admin…) as they are added.
const scriptEntries: Record<string, string> = {
  main: join(client, 'ts/main.ts'),
  captcha: join(client, 'ts/captcha.ts'),
};
const styleEntries: Record<string, string> = {
  main: join(client, 'scss/main.scss'),
};

const scriptOptions: esbuild.BuildOptions = {
  entryPoints: Object.entries(scriptEntries).map(([name, path]) => ({ in: path, out: name })),
  outdir: out,
  bundle: true,
  format: 'esm',
  target: 'es2022',
  minify: !watchMode,
  sourcemap: watchMode ? 'linked' : false,
  legalComments: 'none',
  logLevel: 'info',
};

function buildStyles(): boolean {
  let ok = true;
  for (const [name, entry] of Object.entries(styleEntries)) {
    try {
      const result = sass.compile(entry, {
        style: watchMode ? 'expanded' : 'compressed',
        sourceMap: watchMode,
        sourceMapIncludeSources: watchMode,
      });
      const cssPath = join(out, `${name}.css`);
      if (watchMode && result.sourceMap) {
        writeFileSync(`${cssPath}.map`, JSON.stringify(result.sourceMap));
        writeFileSync(cssPath, `${result.css}\n/*# sourceMappingURL=${name}.css.map */\n`);
      } else {
        writeFileSync(cssPath, result.css);
      }
      console.log(`[scss] ${name}.css`);
    } catch (error) {
      ok = false;
      console.error(`[scss] ${name}: ${error instanceof Error ? error.message : String(error)}`);
    }
  }
  return ok;
}

rmSync(out, { recursive: true, force: true });
mkdirSync(out, { recursive: true });

if (watchMode) {
  const context = await esbuild.context(scriptOptions);
  await context.watch();
  buildStyles();
  let pending: NodeJS.Timeout | undefined;
  watch(client, { recursive: true }, (_event, file) => {
    if (!file?.endsWith('.scss')) return;
    clearTimeout(pending);
    pending = setTimeout(buildStyles, 50);
  });
  console.log('Watching src/Beeps/Client for changes…');
} else {
  await esbuild.build(scriptOptions);
  if (!buildStyles()) process.exitCode = 1;
}
