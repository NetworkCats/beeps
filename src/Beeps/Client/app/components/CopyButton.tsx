// The button of a copy box (<div class="copy-box">), which copies its text and says so for a moment.
import { useEffect, useRef, useState } from 'preact/hooks';
import { copyText } from '../clipboard.ts';
import { t } from '../i18n.ts';

const COPIED_MS = 1500;

export function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);
  const timer = useRef<number | undefined>(undefined);
  useEffect(() => () => window.clearTimeout(timer.current), []);
  const copy = async (): Promise<void> => {
    if (!(await copyText(text))) return;
    setCopied(true);
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => setCopied(false), COPIED_MS);
  };
  return (
    <button class="btn-copy" type="button" data-copied={copied ? '' : undefined} onClick={() => void copy()}>
      {copied ? t('Copy.Done') : t('Copy.Action')}
    </button>
  );
}
