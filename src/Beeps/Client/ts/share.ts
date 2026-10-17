// <a href="/beeps/…" data-share data-share-done="Link copied">: copies the beep's full address instead of opening
// it, and says so in a toast. Where the clipboard cannot be written (no permission, an old browser) it stays a link.
import { showToast } from './toast.ts';

export function initShare(): void {
  document.addEventListener('click', (event) => {
    const link = event.target instanceof Element ? event.target.closest<HTMLAnchorElement>('a[data-share]') : null;
    if (!link || !navigator.clipboard || event.metaKey || event.ctrlKey || event.shiftKey) return;
    event.preventDefault();
    navigator.clipboard.writeText(link.href).then(
      () => showToast(link.dataset.shareDone ?? ''),
      () => window.location.assign(link.href),
    );
  });
}
