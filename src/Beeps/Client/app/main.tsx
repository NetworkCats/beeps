// The site's script (PLAN 6.9, 6.10). Pages are drawn by the server, and each island on them (Pages/Islands) is then
// taken over by the Preact component registered here. Signed in, the islands follow the site over the live
// connection; signed out, the pages stay static. Moving between pages happens in place (router.ts), so the app and its
// connection stay.
import { readConfig } from './config.ts';
import { mountIslands } from './islands.tsx';
import { startLive } from './live.ts';
import { startRouter } from './router.ts';
import { startTheme } from './state.ts';
import { startClock } from './components/Time.tsx';
import { registerShell } from './shell/index.ts';
import { initAuthSync } from './auth-sync.ts';
import { showAnew } from './page.ts';
import { registerPages } from './pages/index.ts';

registerShell();
registerPages();

// Beeps open when clicked anywhere on them, so they react to the pointer like a link.
document.documentElement.dataset.beepLinks = '';
readConfig();
startTheme();
startClock();
startRouter();
mountIslands(document.body);
startLive();
// Signed out, or in as someone else, in another tab: this page is shown again as it now is.
initAuthSync(() => void showAnew());
