// The passkey button on the login page and on a sign-in's second step (Pages/Shared/_PasskeySignIn.cshtml): asks the
// server for the options, the browser for a passkey, and posts what it signed as the login form is posted, the answer
// being the page signed in, or this one saying what went wrong. Where the browser has no passkeys at all, the login
// page's button goes, and the second step says so instead.
import { useEffect, useState } from 'preact/hooks';
import { Icon } from '../components/Icon.tsx';
import { t } from '../i18n.ts';
import type { IslandProps } from '../islands.tsx';
import { getPasskey, PasskeyError, passkeysSupported } from '../passkeys.ts';
import { submitPage } from '../router.ts';
import { showToast } from '../toasts.ts';

interface PasskeySignInProps {
  /** Where the options are asked for. */
  options: string;
  /** Where what the authenticator signed is posted. */
  action: string;
  antiforgery: { field: string; token: string };
  returnUrl?: string;
  /** After the password: one of the account's own passkeys. */
  secondStep?: boolean;
}

export function PasskeySignIn({ props }: IslandProps<PasskeySignInProps>) {
  const [supported, setSupported] = useState(true);
  const [busy, setBusy] = useState(false);
  useEffect(() => setSupported(passkeysSupported()), []);

  if (!supported) return props.secondStep ? <p class="help">{t('Passkeys.Unsupported')}</p> : null;

  const fields = (extra: Record<string, string> = {}): FormData => {
    const data = new FormData();
    data.set(props.antiforgery.field, props.antiforgery.token);
    if (props.returnUrl) data.set('ReturnUrl', props.returnUrl);
    for (const [name, value] of Object.entries(extra)) data.set(name, value);
    return data;
  };

  const signIn = async (): Promise<void> => {
    setBusy(true);
    try {
      const answer = await getPasskey(props.options, fields());
      if (answer) await submitPage(props.action, fields({ Credential: answer.credential, Ticket: answer.ticket }));
    } catch (error) {
      showToast(error instanceof PasskeyError ? error.message : t('Passkeys.Failed'), true);
    } finally {
      setBusy(false);
    }
  };

  return (
    <button class="btn btn-block btn-oauth" type="button" disabled={busy} onClick={() => void signIn()}>
      <Icon name="passkey" />
      {t(props.secondStep ? 'Passkey.Verify' : 'Passkey.SignIn')}
    </button>
  );
}
