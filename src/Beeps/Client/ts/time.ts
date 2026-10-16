// The only place times are turned into human-readable text. The server writes
// <time datetime="2026-10-16T08:30:00Z">2026-10-16T08:30:00Z</time> (UTC, as fallback text);
// this module replaces the text with a relative time in the page language and the viewer's
// own time zone, keeps it fresh every minute, and picks up <time> elements added later (SSE).

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;
// Beyond a week "12 days ago" is harder to place than a date.
const RELATIVE_LIMIT = 7 * DAY;
const SELECTOR = 'time[datetime]';

const relativeFormats = new Map<string, Intl.RelativeTimeFormat>();
const dateFormats = new Map<string, Intl.DateTimeFormat>();

function relativeFormat(locale: string): Intl.RelativeTimeFormat {
  let format = relativeFormats.get(locale);
  if (!format) {
    format = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });
    relativeFormats.set(locale, format);
  }
  return format;
}

function dateFormat(locale: string, kind: 'short' | 'short-year' | 'full'): Intl.DateTimeFormat {
  const key = `${locale}|${kind}`;
  let format = dateFormats.get(key);
  if (!format) {
    const options: Intl.DateTimeFormatOptions =
      kind === 'full' ? { dateStyle: 'full', timeStyle: 'long' }
      : kind === 'short' ? { month: 'short', day: 'numeric' }
      : { year: 'numeric', month: 'short', day: 'numeric' };
    format = new Intl.DateTimeFormat(locale, options);
    dateFormats.set(key, format);
  }
  return format;
}

/** Display text for `date` as seen at `now`: relative within a week, otherwise a date. */
export function formatTime(date: Date, now: Date, locale: string): string {
  const diff = date.getTime() - now.getTime();
  const distance = Math.abs(diff);
  if (distance >= RELATIVE_LIMIT) {
    const sameYear = date.getFullYear() === now.getFullYear();
    return dateFormat(locale, sameYear ? 'short' : 'short-year').format(date);
  }
  const relative = relativeFormat(locale);
  if (distance < MINUTE) return relative.format(0, 'second');
  if (distance < HOUR) return relative.format(Math.trunc(diff / MINUTE), 'minute');
  if (distance < DAY) return relative.format(Math.trunc(diff / HOUR), 'hour');
  return relative.format(Math.trunc(diff / DAY), 'day');
}

/** Full local date and time, shown on hover. */
export function formatFullTime(date: Date, locale: string): string {
  return dateFormat(locale, 'full').format(date);
}

function pageLocale(): string {
  return document.documentElement.lang || navigator.language;
}

function render(element: HTMLTimeElement, now: Date, locale: string): void {
  const date = new Date(element.dateTime);
  if (Number.isNaN(date.getTime())) return;
  element.textContent = formatTime(date, now, locale);
  element.title = formatFullTime(date, locale);
}

/** Formats every `<time datetime>` in `root`. */
export function renderTimes(root: ParentNode, now: Date = new Date()): void {
  const locale = pageLocale();
  if (root instanceof HTMLTimeElement && root.matches(SELECTOR)) render(root, now, locale);
  for (const element of root.querySelectorAll<HTMLTimeElement>(SELECTOR)) render(element, now, locale);
}

export function initTime(): void {
  renderTimes(document);
  setInterval(() => renderTimes(document), MINUTE);
  // Setting textContent only adds text nodes, so this never re-triggers itself.
  new MutationObserver((records) => {
    for (const record of records) {
      for (const node of record.addedNodes) {
        if (node instanceof Element) renderTimes(node);
      }
    }
  }).observe(document.body, { childList: true, subtree: true });
}
