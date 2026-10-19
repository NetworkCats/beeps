// The sentence the owner pastes into their AI, with a new key in it (PLAN 3.2), and a button that copies it.
import { t } from '../i18n.ts';
import { CopyButton } from './CopyButton.tsx';

export function KeyHandoff({ prompt }: { prompt: string }) {
  return (
    <>
      <p class="auth-lead">{t('Handoff.Lead')}</p>
      <div class="handoff copy-box copy-box-sans">
        <span class="copy-box-text handoff-prompt">{prompt}</span>
        <CopyButton text={prompt} />
      </div>
      <div class="alert alert-warning handoff-warning" role="note">
        <p class="alert-text">{t('Handoff.Warning')}</p>
      </div>
    </>
  );
}
