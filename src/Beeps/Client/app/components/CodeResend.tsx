// "No email? Send a new code": a form of its own under a code's form (Onboarding/Verify, the settings' email page).
// Until another code may be sent (AccountService.ResendInterval, which the server also holds to) the button counts
// down and cannot be pressed.
import { useEffect, useState } from 'preact/hooks';
import { sending } from '../forms.ts';
import { t } from '../i18n.ts';
import { Antiforgery } from './Forms.tsx';

/** Whole seconds left until `at` (an ISO time), drawn again each second until none are left. */
function useSecondsUntil(at: string | undefined): number {
  const until = at ? Date.parse(at) : 0;
  const [, tick] = useState(0);
  useEffect(() => {
    if (until <= Date.now()) return;
    const timer = window.setInterval(() => {
      tick((n) => n + 1);
      if (Date.now() >= until) window.clearInterval(timer);
    }, 1000);
    return () => window.clearInterval(timer);
  }, [until]);
  return Math.max(0, Math.ceil((until - Date.now()) / 1000));
}

export function CodeResend({ class: className, action, resendAt }: { class: string; action: string; resendAt?: string }) {
  const left = useSecondsUntil(resendAt);
  return (
    <form class={className} method="post" action={action} onSubmit={sending()}>
      <span>{t('Verify.NoEmail')}</span>
      <button class="btn btn-ghost btn-sm" type="submit" disabled={left > 0}>
        {left > 0 ? t('Verify.ResendIn', left) : t('Verify.Resend')}
      </button>
      <Antiforgery />
    </form>
  );
}
