// A time as <utc-time> writes it, shown as time-format.ts formats it: relative within a week, a date otherwise, in
// the page language and the viewer's own time zone, the full date and time on hover.
import { signal } from '@preact/signals';
import { formatFullTime, formatTime } from '../time-format.ts';

const MINUTE = 60_000;

/** Now, to the minute: every time on the page moves on with it. */
const now = signal(Date.now());

export function startClock(): void {
  window.setInterval(() => {
    now.value = Date.now();
  }, MINUTE);
}

export function Time({ value }: { value: string }) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return <time datetime={value}>{value}</time>;
  const locale = document.documentElement.lang || navigator.language;
  return (
    <time datetime={value} title={formatFullTime(date, locale)}>
      {formatTime(date, new Date(now.value), locale)}
    </time>
  );
}
