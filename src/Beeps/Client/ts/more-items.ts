// The parts of more.ts that need no browser, so Node can test them. Items are named by their keys (a beep's or an
// inbox entry's id, data-key otherwise); an item without one belongs to the keyed item before it (a thread's reply
// box, under its beep).

type Key = string | undefined;

interface Group {
  key: Key;
  indices: number[];
}

function groups(keys: readonly Key[]): Group[] {
  const all: Group[] = [];
  keys.forEach((key, index) => {
    const last = all.at(-1);
    if (key === undefined && last) last.indices.push(index);
    else all.push({ key, indices: [index] });
  });
  return all;
}

/** Which items of a fetched page to add to the list: all but those the page shows already. */
export function itemsToAdd(shown: ReadonlySet<string>, fetched: readonly Key[]): number[] {
  return groups(fetched).filter((group) => group.key === undefined || !shown.has(group.key)).flatMap((group) => group.indices);
}

/**
 * The list drawn again from a fresh copy of its first page: which of the items it showed stay after that page. Those
 * past the last one the fresh page has (so ones that new items pushed off the first page are not lost), or all of them
 * when it has none of them; never one the fresh page has.
 */
export function itemsToKeep(shown: readonly Key[], fresh: readonly Key[]): number[] {
  const freshKeys = new Set(fresh.filter((key) => key !== undefined));
  const last = fresh.findLast((key) => key !== undefined);
  const all = groups(shown);
  const at = last === undefined ? -1 : all.findIndex((group) => group.key === last);
  return all.slice(at + 1).filter((group) => group.key === undefined || !freshKeys.has(group.key)).flatMap((group) => group.indices);
}
