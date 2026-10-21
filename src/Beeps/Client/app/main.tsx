// The site's script. Each island of a page (Pages/Islands) is drawn by the Preact component
// registered here. Signed out, the server draws the pages, islands too, and the components take them over; they stay
// static. Signed in, the server sends the layout alone (an app shell), and the app draws each page from its data
// (JSON from /page-data); its islands follow the site over the live connection. Moving between pages happens in place
// (router.ts), so the app and its connection stay.
//
// The app itself holds only what every page needs. Each page's component, the live connection and what only signed-in
// people or admins see are chunks, loaded when a page first needs them. Once the page has loaded, all the others are
// fetched ahead: the next page (or signing in) then needs nothing more from the network. The admin pages' only for an
// admin: no one else is given them.
import { readConfig, viewer } from './config.ts';
import { loadStrings } from './i18n.ts';
import { mountIslands, preloadIslands } from './islands.tsx';
import { showFirstPage, startRouter } from './router.ts';
import { startTheme } from './state.ts';
import { startClock } from './components/Time.tsx';
import { registerShell } from './shell/index.ts';
import { initAuthSync } from './auth-sync.ts';
import { showAnew } from './page.ts';
import { registerPages } from './pages/index.ts';

registerShell();
registerPages();

const loadLive = () => import('./live.ts');

/** Follows the site live once its code has loaded; a failed load (the network dropped a moment) is tried again. */
function followLive(attempt = 0): void {
  loadLive().then(
    (live) => live.startLive(),
    () => window.setTimeout(() => followLive(attempt + 1), Math.min(60_000, 1000 * 2 ** attempt)),
  );
}

/** Once the page has loaded and the browser has nothing better to do: every script and stylesheet this person may use. */
function fetchAhead(): void {
  const idle = window.requestIdleCallback ?? ((run: () => void) => window.setTimeout(run, 200));
  const start = (): void => {
    idle(() => {
      preloadIslands(viewer.value.admin);
      loadLive().catch(() => {});
      // The avatar and banner cropper, otherwise imported once a file is picked (components/ProfileImageEditor.tsx).
      import('cropperjs').catch(() => {});
      // The stylesheets this page does not link (<link rel="prefetch">), where the browser does not fetch them itself.
      if (!document.createElement('link').relList.supports('prefetch')) {
        for (const link of document.querySelectorAll<HTMLLinkElement>('link[rel="prefetch"]')) {
          fetch(link.href, { credentials: 'same-origin', priority: 'low' }).catch(() => {});
        }
      }
    });
  };
  if (document.readyState === 'complete') start();
  else window.addEventListener('load', start, { once: true });
}

// Beeps open when clicked anywhere on them, so they react to the pointer like a link.
document.documentElement.dataset.beepLinks = '';
// An app shell has none: it comes with the page's data.
readConfig();
startTheme();
startClock();
startRouter();
// The islands are drawn once the text is there: kept in the browser, or fetched first.
const drawn = document.querySelector('#main[data-app-shell]')
  ? showFirstPage()
  : loadStrings().then(() => mountIslands(document.body));
// Signed in, the page follows the site live, from when its islands have said which beeps they show.
if (document.body.dataset.liveStream) {
  void drawn.catch(() => {}).then(() => followLive());
}
// Signed out, or in as someone else, in another tab: this page is shown again as it now is.
initAuthSync(() => void showAnew());
// Once the page is drawn: until then its own data and code come first, and an app shell's config says who is in.
void drawn.catch(() => {}).then(fetchAhead);
