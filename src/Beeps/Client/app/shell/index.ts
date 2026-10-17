// The islands of the layout, around every page's content (Pages/Shared/_Layout.cshtml), and the forms' Turnstile widget.
// What every page has comes with the app; what only signed-in people (or the sign-in forms) have is loaded when used.
import { island, lazyIsland } from '../islands.tsx';
import { SiteOptions } from './SiteOptions.tsx';
import { Toasts } from './Toasts.tsx';

export function registerShell(): void {
  island('site-options', SiteOptions);
  island('toasts', Toasts);
  lazyIsland('account-menu', () => import('./AccountMenu.tsx').then((m) => m.AccountMenu));
  lazyIsland('inbox-count', () => import('./InboxCount.tsx').then((m) => m.InboxCount));
  lazyIsland('confirm', () => import('./ConfirmDialog.tsx').then((m) => m.ConfirmDialog));
  lazyIsland('captcha', () => import('./Captcha.tsx').then((m) => m.Captcha));
}
