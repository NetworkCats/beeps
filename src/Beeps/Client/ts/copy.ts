// Copy to clipboard, adapted from collection-ui's copy.ts (no toast, no icon font).
//   <div class="copy-box" data-copy><code>…</code><button class="btn-copy" data-copy-label="Copied">Copy</button></div>
// Clicking the button copies the box's text (.copy-box-text or <code>); the button shows its
// data-copy-label and gets data-copied for a moment.

const RESET_MS = 1500;
const resetTimers = new WeakMap<HTMLElement, ReturnType<typeof setTimeout>>();

function legacyCopy(text: string): boolean {
  // Fallback for insecure contexts (plain http on a LAN address, some webviews).
  const scratch = document.createElement('textarea');
  scratch.value = text;
  scratch.readOnly = true;
  scratch.className = 'visually-hidden';
  document.body.append(scratch);
  scratch.select();
  try {
    return document.execCommand('copy');
  } catch {
    return false;
  } finally {
    scratch.remove();
  }
}

export async function copyText(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    return legacyCopy(text);
  }
}

function markCopied(button: HTMLElement): void {
  const label = button.dataset.copyLabel;
  const original = button.dataset.copyOriginal ?? button.textContent ?? '';
  button.dataset.copyOriginal = original;
  button.setAttribute('data-copied', '');
  if (label) button.textContent = label;
  clearTimeout(resetTimers.get(button));
  resetTimers.set(button, setTimeout(() => {
    button.removeAttribute('data-copied');
    button.textContent = original;
  }, RESET_MS));
}

export function initCopy(): void {
  document.addEventListener('click', (event) => {
    const button = (event.target as Element | null)?.closest<HTMLElement>('[data-copy] .btn-copy');
    const box = button?.closest<HTMLElement>('[data-copy]');
    if (!button || !box) return;
    event.preventDefault();
    const source = box.querySelector('.copy-box-text, code');
    const text = (source?.textContent ?? '').trim();
    if (!text) return;
    void copyText(text).then((ok) => {
      if (ok) markCopied(button);
    });
  });
}
