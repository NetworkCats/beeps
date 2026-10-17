// <a class="column-back" href="…">: goes back to the page this one was opened from, as the browser's own back
// button would, where it was left scrolled. Its href is where to go when there is no such page on this site (opened
// directly, in a new tab, from elsewhere) or when that page is a form just sent, which would only be sent again.

const POSTED_FROM = 'beeps:posted-from';

/** An address without its fragment: the same page, wherever it was scrolled to. */
function pageOf(url: string): string {
  const parsed = new URL(url);
  parsed.hash = '';
  return parsed.toString();
}

/** Whether going back leads to `referrer`, a page of this site worth returning to. */
export function returnsToReferrer(referrer: string, here: string, postedFrom: string | null): boolean {
  if (!referrer || new URL(referrer).origin !== new URL(here).origin) return false;
  const previous = pageOf(referrer);
  return previous !== pageOf(here) && previous !== postedFrom;
}

function takePostedFrom(): string | null {
  try {
    const page = sessionStorage.getItem(POSTED_FROM);
    sessionStorage.removeItem(POSTED_FROM);
    return page;
  } catch {
    return null;
  }
}

export function initBack(): void {
  const postedFrom = takePostedFrom();
  // On the window, so handlers that send the form themselves (and cancel it) have run by now.
  window.addEventListener('submit', (event) => {
    if (event.defaultPrevented || !(event.target instanceof HTMLFormElement) || event.target.method !== 'post') return;
    try {
      sessionStorage.setItem(POSTED_FROM, pageOf(window.location.href));
    } catch {
      // Without storage, going back may show the form again; the browser's back button would too.
    }
  });
  document.addEventListener('click', (event) => {
    const link = event.target instanceof Element ? event.target.closest<HTMLAnchorElement>('a.column-back') : null;
    if (!link || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    if (window.history.length < 2 || !returnsToReferrer(document.referrer, window.location.href, postedFrom)) return;
    event.preventDefault();
    window.history.back();
  });
}
