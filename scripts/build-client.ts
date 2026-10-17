// Builds the client assets into src/Beeps/wwwroot/dist. Node runs this file directly.
//
//   node scripts/build-client.ts           one-shot production build (minified, no source maps)
//   node scripts/build-client.ts --watch   rebuild on change, with source maps
//
// File names stay stable (app.js, main.css): MapStaticAssets fingerprints them at `dotnet build`,
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

// One app for every page: server-drawn HTML whose islands Preact takes over, live for signed-in people (PLAN 6.9, 6.10).
const scriptEntries: Record<string, string> = {
  app: join(client, 'app/main.tsx'),
};
const styleEntries: Record<string, string> = {
  main: join(client, 'scss/main.scss'),
  'avatar-crop': join(client, 'scss/avatar-crop.scss'),
};

const scriptOptions: esbuild.BuildOptions = {
  entryPoints: Object.entries(scriptEntries).map(([name, path]) => ({ in: path, out: name })),
  outdir: out,
  bundle: true,
  format: 'esm',
  // Shared code and what only some pages need (Cropper.js for the avatar forms) go into chunks, loaded when used.
  splitting: true,
  chunkNames: 'chunks/[name]-[hash]',
  target: 'es2022',
  jsx: 'automatic',
  jsxImportSource: 'preact',
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
        // Lets a stylesheet @use the CSS that a client library ships (Cropper.js).
        loadPaths: [join(root, 'node_modules')],
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
