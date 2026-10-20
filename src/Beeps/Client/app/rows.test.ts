import { test } from 'node:test';
import assert from 'node:assert/strict';
import type { PageProps } from './props.ts';
import { loadPages } from './rows.ts';

interface Props {
  rows: PageProps<string>;
}

// Five rows, two to a page, as the admin tables' older pages answer: `?before=` the last one shown.
const ROWS = ['a', 'b', 'c', 'd', 'e'];

/** Answers each page's data from ROWS, and says which pages were asked for. */
function serve(island = 'admin-agents'): string[] {
  const asked: string[] = [];
  Object.assign(globalThis, {
    window: { location: new URL('https://beeps.test/admin/agents') },
    fetch: async (url: string) => {
      const before = new URL(url).searchParams.get('before');
      asked.push(before ?? '');
      const from = before ? ROWS.indexOf(before) + 1 : 0;
      const items = ROWS.slice(from, from + 2);
      const next = from + 2 < ROWS.length ? `/admin/agents?before=${items.at(-1)}` : undefined;
      const page = { islands: { [island]: { props: { rows: { items, next } } } } };
      return new Response(JSON.stringify(page), { headers: { 'Content-Type': 'application/json' } });
    },
  });
  return asked;
}

const pick = (props: Props): PageProps<string> => props.rows;

test('the rows past the first page load again, as many as there were', async () => {
  const asked = serve();
  assert.deepEqual(await loadPages('/admin/agents?before=b', 3, 'admin-agents', pick), { items: ['c', 'd', 'e'], next: undefined });
  assert.deepEqual(asked, ['b', 'd']);
});

test('they stop at the page that has enough, with the next page from where it ends', async () => {
  const asked = serve();
  assert.deepEqual(await loadPages('/admin/agents?before=b', 1, 'admin-agents', pick), { items: ['c', 'd'], next: '/admin/agents?before=d' });
  assert.deepEqual(asked, ['b']);
});

test('a page that is not the island, or that is no longer wanted, gives nothing', async () => {
  serve('admin-system');
  assert.equal(await loadPages('/admin/agents?before=b', 3, 'admin-agents', pick), undefined);
  serve();
  assert.equal(await loadPages('/admin/agents?before=b', 3, 'admin-agents', pick, () => false), undefined);
});
