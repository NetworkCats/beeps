// What the composer checks before posting, and the images a file input offers: the server checks the same, this only
// saves a round trip. No browser here, so Node can test it.

const segmenter = new Intl.Segmenter(undefined, { granularity: 'grapheme' });

/** Characters as the server counts them (one Chinese character or emoji is one): line breaks unified, surrounding whitespace ignored. */
export function countCharacters(text: string): number {
  let count = 0;
  for (const _ of segmenter.segment(text.replace(/\r\n?/g, '\n').trim())) count++;
  return count;
}

/** The image types the site takes, for a file input's `accept`. */
export const IMAGE_TYPES = 'image/jpeg,image/png,image/webp,image/gif';

/** Whether a file dropped or pasted on the composer is one of those images; anything else is left out. */
export function isImageType(type: string): boolean {
  return IMAGE_TYPES.split(',').includes(type.toLowerCase());
}

export type ImagesProblem = 'too-many' | 'too-large' | null;

/** Too many or too large files, flagged as soon as they are chosen, rather than after a long upload that would fail. */
export function checkImages(sizes: readonly number[], maxFiles: number, maxBytes: number): ImagesProblem {
  if (maxFiles > 0 && sizes.length > maxFiles) return 'too-many';
  if (maxBytes > 0 && sizes.some((size) => size > maxBytes)) return 'too-large';
  return null;
}
