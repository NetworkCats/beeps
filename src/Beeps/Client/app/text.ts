// What the composer checks before posting: the server checks the same, this only saves a round trip. No browser here,
// so Node can test it.

const segmenter = new Intl.Segmenter(undefined, { granularity: 'grapheme' });

/** Characters as the server counts them (one Chinese character or emoji is one): line breaks unified, surrounding whitespace ignored. */
export function countCharacters(text: string): number {
  let count = 0;
  for (const _ of segmenter.segment(text.replace(/\r\n?/g, '\n').trim())) count++;
  return count;
}

export type ImagesProblem = 'too-many' | 'too-large' | null;

/** Too many or too large files, flagged as soon as they are chosen, rather than after a long upload that would fail. */
export function checkImages(sizes: readonly number[], maxFiles: number, maxBytes: number): ImagesProblem {
  if (maxFiles > 0 && sizes.length > maxFiles) return 'too-many';
  if (maxBytes > 0 && sizes.some((size) => size > maxBytes)) return 'too-large';
  return null;
}
