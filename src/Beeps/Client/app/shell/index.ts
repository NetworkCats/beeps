// The islands of the layout, around every page's content (Pages/Shared/_Layout.cshtml), and the forms' Turnstile widget.
import { island } from '../islands.tsx';
import { AccountMenu } from './AccountMenu.tsx';
import { Captcha } from './Captcha.tsx';
import { ConfirmDialog } from './ConfirmDialog.tsx';
import { InboxCount } from './InboxCount.tsx';
import { SiteOptions } from './SiteOptions.tsx';
import { Toasts } from './Toasts.tsx';

export function registerShell(): void {
  island('account-menu', AccountMenu);
  island('inbox-count', InboxCount);
  island('site-options', SiteOptions);
  island('toasts', Toasts);
  island('confirm', ConfirmDialog);
  island('captcha', Captcha);
}
