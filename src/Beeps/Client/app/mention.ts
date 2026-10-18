// The @mention being typed in a composer, read as the server reads mentions (Features/Beeps/BeepText.cs): an @ at the
// start or after something that is not part of a word, then a username, and for an agent `.name`. No browser here, so
// Node can test it.

/** The mention the caret is in: from its @ to its last character, and the handle typed up to the caret. */
export interface MentionAt {
  start: number;
  end: number;
  query: string;
}

const HANDLE_MAX_LENGTH = 64;
const HANDLE_CHARACTER = /[A-Za-z0-9_]/;
const HANDLE_PREFIX = /^[A-Za-z0-9_]+(\.[A-Za-z0-9_]*)?$/;
const WORD_CHARACTER = /[\p{L}\p{N}\p{M}_]/u;

/** Whether a character may run into what follows it, so that an @ after it starts no mention (`a@b.com`). */
function joins(character: string | undefined): boolean {
  return character !== undefined && (WORD_CHARACTER.test(character) || '&/\\#@'.includes(character));
}

function handleRun(text: string, from: number): number {
  let end = from;
  while (end < text.length && HANDLE_CHARACTER.test(text[end] ?? '')) end++;
  return end - from;
}

/** Where the text has mentions, drawn in colour as it is typed: each from its @ to its last character. */
export function mentionRanges(text: string): Array<{ start: number; end: number }> {
  const ranges: Array<{ start: number; end: number }> = [];
  for (let at = text.indexOf('@'); at !== -1; at = text.indexOf('@', at + 1)) {
    if (joins([...text.slice(Math.max(0, at - 2), at)].pop())) continue;
    let length = handleRun(text, at + 1);
    if (length === 0) continue;
    const dot = at + 1 + length;
    const name = text[dot] === '.' ? handleRun(text, dot + 1) : 0;
    if (name > 0) length += 1 + name;
    if (length > HANDLE_MAX_LENGTH) continue;
    ranges.push({ start: at, end: at + 1 + length });
    at += length;
  }
  return ranges;
}

/** The mention the caret (with nothing selected) is in, once at least a letter of it is typed; null outside one. */
export function mentionAt(text: string, caret: number): MentionAt | null {
  let start = caret;
  let dot = false;
  for (; start > 0; start--) {
    const character = text[start - 1] ?? '';
    if (character === '.' && !dot) dot = true;
    else if (!HANDLE_CHARACTER.test(character)) break;
  }
  if (text[start - 1] !== '@' || joins([...text.slice(Math.max(0, start - 3), start - 1)].pop())) return null;
  const query = text.slice(start, caret);
  if (!HANDLE_PREFIX.test(query)) return null;

  // The rest of the handle after the caret goes too when a suggestion takes its place.
  let end = caret;
  for (; end < text.length; end++) {
    const character = text[end] ?? '';
    if (character === '.' && !dot && HANDLE_CHARACTER.test(text[end + 1] ?? '')) dot = true;
    else if (!HANDLE_CHARACTER.test(character)) break;
  }
  return end - start > HANDLE_MAX_LENGTH ? null : { start: start - 1, end, query };
}

/** The text with the mention completed to `handle`, and the caret after it and the space that follows, as on Twitter. */
export function completeMention(text: string, mention: MentionAt, handle: string): { text: string; caret: number } {
  const rest = text.slice(mention.end);
  const spaced = /^\s/.test(rest);
  const completed = `@${handle}${spaced ? '' : ' '}`;
  return { text: text.slice(0, mention.start) + completed + rest, caret: mention.start + completed.length + (spaced ? 1 : 0) };
}
