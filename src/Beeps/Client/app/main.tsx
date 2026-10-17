// The site's script (PLAN 6.9, 6.10). Pages are drawn by the server, and each island on them (Pages/Islands) is then
// taken over by the Preact component registered here. Signed in, the islands follow the site over the live
// connection; signed out, the pages stay static. Moving between pages happens in place (router.ts), so the app and its
// connection stay.
//
// The app itself holds only what every page needs. Each page's component, the live connection and what only signed-in
// people or admins see are chunks, loaded when a page first needs them. Once the page has loaded, all the others are
// fetched ahead: the next page (or signing in) then needs nothing more from the network. The admin pages' only for an
// admin: no one else is given them.
import { readConfig, viewer } from './config.ts';
import { mountIslands, preloadIslands } from './islands.tsx';
import { startRouter } from './router.ts';
import { startTheme } from './state.ts';
import { startClock } from './components/Time.tsx';
import { registerShell } from './shell/index.ts';
import { initAuthSync } from './auth-sync.ts';
import { showAnew } from './page.ts';
import { registerPages } from './pages/index.ts';

registerShell();
registerPages();

const loadLive = () => import('./live.ts');

/** Once the page has loaded and the browser has nothing better to do: every script and stylesheet this person may use. */
function fetchAhead(): void {
  const idle = window.requestIdleCallback ?? ((run: () => void) => window.setTimeout(run, 200));
  const start = (): void => {
    idle(() => {
      preloadIslands(viewer.value.admin);
      loadLive().catch(() => {});
      // The avatar cropper, otherwise imported once a file is picked (components/AvatarEditor.tsx).
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
readConfig();
startTheme();
startClock();
startRouter();
const drawn = mountIslands(document.body);
// Signed in, the page follows the site live, from when its islands have said which beeps they show.
if (document.body.dataset.liveStream) {
  void drawn.catch(() => {}).then(() => loadLive()).then((live) => live.startLive());
}
// Signed out, or in as someone else, in another tab: this page is shown again as it now is.
initAuthSync(() => void showAnew());
fetchAhead();
