// The only place times are turned into human-readable text. The server writes
// <time datetime="2026-10-16T08:30:00Z">2026-10-16T08:30:00Z</time> (UTC, as fallback text); components/Time.tsx
// shows it with these: a relative time within a week, a date otherwise, in the page language and the viewer's own
// time zone. No browser here, so Node can test it.

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;
// Beyond a week "12 days ago" is harder to place than a date.
const RELATIVE_LIMIT = 7 * DAY;

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
