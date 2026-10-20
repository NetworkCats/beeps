// Builds the client assets into src/Beeps/wwwroot/dist. Node runs this file directly.
//
//   node scripts/build-client.ts           one-shot production build (minified, no source maps)
//   node scripts/build-client.ts --watch   rebuild on change, with source maps (and Client/strings.json, build-strings.ts)
//
// File names stay stable (app.js, main.css): MapStaticAssets fingerprints them at `dotnet build`,
// so cache busting is not this script's job. Chunks carry a hash of their content in their names.
//
// Besides the assets it writes chunks.json: the chunks app.js imports, and for each island whose component is a chunk
// of its own (lazyIsland or adminIsland in Client/app) the chunks it needs beyond the app. The server names them in the
// pages that need them (<link rel="modulepreload">, Pages/Islands/ClientChunks.cs), so they load alongside app.js
// instead of one after another as each import is found. It also lists the chunks only the admin pages use, which the
// server gives to admins only (Pages/Islands/ClientAssetsMiddleware.cs).
import * as esbuild from 'esbuild';
import * as sass from 'sass';
import { spawnSync } from 'node:child_process';
import { mkdirSync, readFileSync, rmSync, watch, writeFileSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { appSources, client, isAppSource, root } from './client-sources.ts';

const out = join(root, 'src/Beeps/wwwroot/dist');
const watchMode = process.argv.includes('--watch');

// One app for every page: signed out, server-drawn HTML whose islands Preact takes over; signed in, an app shell drawn
// from page data and kept live (PLAN 6.9, 6.10).
const appEntry = join(client, 'app/main.tsx');
const scriptEntries: Record<string, string> = {
  app: appEntry,
};
// main for everyone; member and admin only where they are used (Pages/Shared/_Layout.cshtml), image-crop on the pages
// with avatar and banner forms.
const styleEntries: Record<string, string> = {
  main: join(client, 'scss/main.scss'),
  member: join(client, 'scss/member.scss'),
  admin: join(client, 'scss/admin.scss'),
  'image-crop': join(client, 'scss/image-crop.scss'),
};

// lazyIsland('name', () => import('./File.tsx')…), or adminIsland: the island and the module its chunk starts from.
const lazyIslandCall = /\b(lazyIsland|adminIsland)\(\s*'([^']+)',\s*\(\)\s*=>\s*import\('([^']+)'\)/g;

interface LazyIsland {
  /** Relative to the root, as esbuild's metafile names it. */
  module: string;
  admin: boolean;
}

/** Each lazy island's module, and whether it is an admin page's. */
function lazyIslands(): Map<string, LazyIsland> {
  const islands = new Map<string, LazyIsland>();
  for (const path of appSources()) {
    const file = relative(client, path);
    const source = readFileSync(path, 'utf8');
    const calls = source.match(/\b(lazyIsland|adminIsland)\(\s*'/g)?.length ?? 0;
    let found = 0;
    for (const [, kind = '', name = '', module = ''] of source.matchAll(lazyIslandCall)) {
      islands.set(name, { module: relative(root, resolve(dirname(path), module)), admin: kind === 'adminIsland' });
      found++;
    }
    // Anything but the plain form would leave an island without its preload (or an admin chunk unguarded): better to
    // fail the build.
    if (calls !== found) throw new Error(`${file}: a lazy island is not registered as lazyIsland('name', () => import('./File.tsx')…)`);
  }
  return islands;
}

function writeChunkManifest(metafile: esbuild.Metafile): void {
  const dist = relative(root, out) + '/';
  const outputs = metafile.outputs;
  const byModule = new Map(Object.entries(outputs).filter(([, o]) => o.entryPoint).map(([file, o]) => [o.entryPoint!, file]));
  /** A chunk and what it imports in turn, without the app's own (loaded with it). */
  const closure = (file: string, into: Set<string>): Set<string> => {
    if (into.has(file)) return into;
    into.add(file);
    for (const imported of outputs[file]?.imports ?? []) {
      if (imported.kind === 'import-statement') closure(imported.path, into);
    }
    return into;
  };
  const appFile = byModule.get(relative(root, appEntry))!;
  const app = closure(appFile, new Set());
  const name = (file: string): string => file.slice(dist.length);
  const islands: Record<string, string[]> = {};
  const adminChunks = new Set<string>();
  for (const [island, { module, admin }] of lazyIslands()) {
    const chunk = byModule.get(module);
    if (!chunk) throw new Error(`The island ${island}: no chunk for ${module}`);
    if (admin) adminChunks.add(chunk);
    islands[island] = [...closure(chunk, new Set())].filter((file) => !app.has(file)).map(name);
  }
  // Everything the app, the other islands, the live connection or the cropper may load is for everyone; what is left
  // only the admin pages use.
  const forEveryone = new Set<string>();
  for (const [file, output] of Object.entries(outputs)) {
    if (output.entryPoint && !adminChunks.has(file)) closure(file, forEveryone);
  }
  const admin = [...Object.keys(outputs).filter((file) => file.endsWith('.js') && !forEveryone.has(file)).map(name).sort(), 'admin.css'];
  const manifest = { app: [...app].filter((file) => file !== appFile).map(name), islands, admin };
  writeFileSync(join(out, 'chunks.json'), `${JSON.stringify(manifest, null, 2)}\n`);
}

const scriptOptions: esbuild.BuildOptions = {
  absWorkingDir: root,
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
  metafile: true,
  plugins: [{
    name: 'chunk-manifest',
    setup(build) {
      build.onEnd((result) => {
        if (result.metafile) writeChunkManifest(result.metafile);
      });
    },
  }],
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

/** Lists the resource keys again (scripts/build-strings.ts), so the server sends a new string as soon as it is used. */
function buildStrings(): void {
  spawnSync(process.execPath, [join(root, 'scripts/build-strings.ts')], { stdio: 'inherit' });
}

if (watchMode) {
  const context = await esbuild.context(scriptOptions);
  await context.watch();
  buildStrings();
  buildStyles();
  const pending = new Map<() => unknown, NodeJS.Timeout>();
  const soon = (step: () => unknown): void => {
    clearTimeout(pending.get(step));
    pending.set(step, setTimeout(step, 50));
  };
  watch(client, { recursive: true }, (_event, file) => {
    if (file?.endsWith('.scss')) soon(buildStyles);
    else if (file && isAppSource(file)) soon(buildStrings);
  });
  console.log('Watching src/Beeps/Client for changes…');
} else {
  await esbuild.build(scriptOptions);
  if (!buildStyles()) process.exitCode = 1;
}
