// The islands of the layout, around every page's content (Pages/Shared/_Layout.cshtml), and the forms' Turnstile widget.
// What every page has comes with the app; what only signed-in people (or the sign-in forms) have is loaded when used,
// except the confirmation dialog: it must be there before the first destructive button can be pressed, or the
// browser's own confirm() would ask instead (dialogs.ts). The lightbox (lightbox.ts) comes with it too: pictures are on
// every page, signed out included.
import { island, lazyIsland } from '../islands.tsx';
import { ConfirmDialog } from './ConfirmDialog.tsx';
import { Lightbox } from './Lightbox.tsx';
import { SiteOptions } from './SiteOptions.tsx';
import { Toasts } from './Toasts.tsx';

export function registerShell(): void {
  island('site-options', SiteOptions);
  island('toasts', Toasts);
  island('confirm', ConfirmDialog);
  island('lightbox', Lightbox);
  lazyIsland('account-menu', () => import('./AccountMenu.tsx').then((m) => m.AccountMenu));
  lazyIsland('inbox-count', () => import('./InboxCount.tsx').then((m) => m.InboxCount));
  lazyIsland('captcha', () => import('./Captcha.tsx').then((m) => m.Captcha));
}
