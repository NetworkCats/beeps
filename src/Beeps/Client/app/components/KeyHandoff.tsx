// The sentence the owner pastes into their AI, with a new key in it (Pages/Shared/_KeyHandoff.cshtml), and a button
// that copies it.
import { useEffect, useRef, useState } from 'preact/hooks';
import { copyText } from '../clipboard.ts';
import { t } from '../i18n.ts';

const RESET_MS = 1500;

export function KeyHandoff({ prompt }: { prompt: string }) {
  const [copied, setCopied] = useState(false);
  const timer = useRef<number | undefined>(undefined);
  useEffect(() => () => window.clearTimeout(timer.current), []);
  const copy = async (): Promise<void> => {
    if (!(await copyText(prompt))) return;
    setCopied(true);
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => setCopied(false), RESET_MS);
  };
  return (
    <>
      <p class="auth-lead">{t('Handoff.Lead')}</p>
      <div class="handoff copy-box copy-box-sans">
        <span class="copy-box-text handoff-prompt">{prompt}</span>
        <button class="btn-copy" type="button" data-copied={copied ? '' : undefined} onClick={() => void copy()}>
          {copied ? t('Copy.Done') : t('Copy.Action')}
        </button>
      </div>
      <div class="alert alert-warning handoff-warning" role="note">
        <p class="alert-text">{t('Handoff.Warning')}</p>
      </div>
    </>
  );
}
