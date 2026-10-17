// Signed out, every island must draw the very markup the server drew (PLAN 6.9), or the page changes as the app takes
// over. The server's tests keep, for pages drawn with fixed data, each island's markup and the props it was drawn from
// (tests/Beeps.Tests/Pages/IslandSamples, IslandSamplesTests.cs). Here the components draw the same props, and the
// two are compared as the browser would see them: attribute order, whitespace between elements, how characters are
// escaped, and what only the browser fills in (relative times, a pager's loading state) set aside. Islands that come
// with props alone (signed in, and pages no search engine needs) are drawn too, to see each draws from what it is sent.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { renderToString } from 'preact-render-to-string';
import { setConfig, type AppConfig } from './config.ts';
import { loadComponent } from './islands.tsx';
import { registerPages } from './pages/index.ts';
import type { Errors } from './props.ts';
import { registerShell } from './shell/index.ts';
import { toasts } from './toasts.ts';

interface Sample {
  url: string;
  config: AppConfig;
  islands: Array<{ name: string; props: unknown; errors?: Errors | null; html: string }>;
}

// Tests run bundled (scripts/test-client.ts) from the repository's root.
const samples = resolve('tests/Beeps.Tests/Pages/IslandSamples');

const VOID = new Set(['area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'source', 'track', 'wbr']);
const BOOLEAN = new Set(['hidden', 'selected', 'checked', 'required', 'multiple', 'autofocus', 'disabled', 'open', 'readonly']);
// Filled in by the browser only: a pager's loading state.
const BROWSER_ONLY = new Set(['data-more-state', 'aria-busy']);
const ENTITIES: Record<string, string> = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: ' ' };

function decode(text: string): string {
  return text.replace(/&(#x[0-9a-f]+|#\d+|[a-z]+);/gi, (match, entity: string) => {
    if (entity[0] === '#') return String.fromCodePoint(entity[1]?.toLowerCase() === 'x' ? parseInt(entity.slice(2), 16) : Number(entity.slice(1)));
    return ENTITIES[entity.toLowerCase()] ?? match;
  });
}

interface Element {
  tag: string;
  attributes: Array<[string, string]>;
  children: Array<Element | string>;
}

function parse(html: string): Element {
  const root: Element = { tag: '#root', attributes: [], children: [] };
  const stack = [root];
  const tokens = /<!--[\s\S]*?-->|<\/([a-zA-Z][\w-]*)\s*>|<([a-zA-Z][\w-]*)((?:\s+[^\s=>/]+(?:\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+))?)*)\s*\/?>|[^<]+/g;
  for (const [token, closing, opening, attributeText] of html.matchAll(tokens)) {
    const parent = stack.at(-1) as Element;
    if (token.startsWith('<!--')) continue;
    if (closing) {
      const at = stack.map((e) => e.tag).lastIndexOf(closing.toLowerCase());
      assert.ok(at > 0, `</${closing}> closes nothing`);
      stack.length = at;
    } else if (opening) {
      const attributes: Array<[string, string]> = [];
      for (const [, name = '', , double, single, bare] of (attributeText ?? '').matchAll(/([^\s=>/]+)(\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+)))?/g)) {
        attributes.push([name.toLowerCase(), decode(double ?? single ?? bare ?? '')]);
      }
      const element: Element = { tag: opening.toLowerCase(), attributes, children: [] };
      parent.children.push(element);
      if (!VOID.has(element.tag)) stack.push(element);
    } else {
      parent.children.push(decode(token));
    }
  }
  return root;
}

/** One line per element and per piece of text, as the browser would lay it out. */
function canonical(node: Element, depth = 0, out: string[] = []): string[] {
  const pad = '  '.repeat(depth);
  // Text split in two by the template or the component is still one text.
  const children: Array<Element | string> = [];
  for (const child of node.children) {
    const last = children.at(-1);
    if (typeof child === 'string' && typeof last === 'string') children[children.length - 1] = last + child;
    else children.push(child);
  }
  for (const child of children) {
    if (typeof child === 'string') {
      const text = child.replace(/\s+/g, ' ').trim();
      if (text) out.push(`${pad}"${node.tag === 'time' ? '<time>' : text}"`);
      continue;
    }
    const attributes = child.attributes
      .filter(([name]) => !BROWSER_ONLY.has(name) && !(child.tag === 'time' && name === 'title'))
      .map(([name, value]) => `${name}="${BOOLEAN.has(name) ? '' : value}"`)
      .sort();
    out.push(`${pad}<${child.tag}${attributes.length ? ` ${attributes.join(' ')}` : ''}>`);
    canonical(child, depth + 1, out);
  }
  return out;
}

function sameMarkup(server: string, island: string, where: string): void {
  const expected = canonical(parse(server));
  const actual = canonical(parse(island));
  const at = expected.findIndex((line, i) => line !== actual[i]);
  if (at < 0 && expected.length === actual.length) return;
  const from = Math.max(0, (at < 0 ? expected.length : at) - 3);
  assert.fail(
    `${where}: the component's markup differs from the server's at line ${at < 0 ? expected.length : at}\n`
      + `server:\n${expected.slice(from, from + 8).join('\n')}\ncomponent:\n${actual.slice(from, from + 8).join('\n')}`,
  );
}

registerShell();
registerPages();

const files = readdirSync(samples).filter((name) => name.endsWith('.json')).sort();

test('there are samples to compare with', () => {
  assert.ok(files.length > 0, `no samples in ${samples}`);
});

for (const file of files) {
  test(`the islands of ${file} draw what the server drew`, async () => {
    const sample = JSON.parse(readFileSync(join(samples, file), 'utf8')) as Sample;
    const location = new URL(sample.url, 'https://beeps.test');
    Object.assign(globalThis, {
      window: { location, setTimeout, clearTimeout },
      document: { documentElement: { lang: 'en', dataset: {} } },
    });
    setConfig(sample.config);
    toasts.value = [];
    for (const island of sample.islands) {
      const Component = await loadComponent(island.name);
      assert.ok(Component, `no component for the island "${island.name}"`);
      const html = renderToString(<Component props={island.props} errors={island.errors ?? {}} />);
      if (island.html) sameMarkup(island.html, html, `${file} ${island.name}`);
      else assert.ok(html.length > 0 || island.name === 'toasts', `${file} ${island.name}: drew nothing`);
    }
  });
}
