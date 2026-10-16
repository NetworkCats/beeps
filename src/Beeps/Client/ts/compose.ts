// The posting form (<textarea data-composer data-max-length="500">): a live count of what is left, in
// the characters people see (one Chinese character or emoji is one), and Ctrl/⌘+Enter to post. The
// server checks the same limit; this only saves a round trip.

const segmenter = new Intl.Segmenter(undefined, { granularity: 'grapheme' });

/** Characters as the server counts them: line breaks unified, surrounding whitespace ignored. */
export function countCharacters(text: string): number {
  let count = 0;
  for (const _ of segmenter.segment(text.replace(/\r\n?/g, '\n').trim())) count++;
  return count;
}

function update(textarea: HTMLTextAreaElement): void {
  const counter = textarea.form?.querySelector<HTMLElement>('[data-composer-count]');
  const max = Number(textarea.dataset.maxLength);
  if (!counter || !max) return;
  const left = max - countCharacters(textarea.value);
  counter.textContent = String(left);
  counter.toggleAttribute('data-over', left < 0);
  // Only near the limit is the number worth reading out.
  counter.hidden = left > max * 0.2;
}

export function initCompose(): void {
  for (const textarea of document.querySelectorAll<HTMLTextAreaElement>('textarea[data-composer]')) update(textarea);
  document.addEventListener('input', (event) => {
    if (event.target instanceof HTMLTextAreaElement && event.target.matches('[data-composer]')) update(event.target);
  });
  document.addEventListener('keydown', (event) => {
    const target = event.target;
    if (event.key !== 'Enter' || !(event.ctrlKey || event.metaKey)) return;
    if (!(target instanceof HTMLTextAreaElement) || !target.matches('[data-composer]')) return;
    event.preventDefault();
    target.form?.requestSubmit();
  });
}
