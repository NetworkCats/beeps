// Languages by name, in the page language: the server sends ISO 639 codes (the language a translated beep was written
// in, the switches of the translation settings), and the browser knows what each is called. No browser here, so Node
// can test it.

const names = new Map<string, Intl.DisplayNames | null>();

function displayNames(locale: string): Intl.DisplayNames | null {
  let found = names.get(locale);
  if (found === undefined) {
    try {
      found = new Intl.DisplayNames([locale], { type: 'language', fallback: 'none' });
    } catch {
      found = null;
    }
    names.set(locale, found);
  }
  return found;
}

/** The language `code` is, as `locale` calls it; the code itself where the browser does not know it. */
export function languageName(code: string, locale: string): string {
  try {
    return displayNames(locale)?.of(code) ?? code;
  } catch {
    // Not a well-formed code.
    return code;
  }
}
