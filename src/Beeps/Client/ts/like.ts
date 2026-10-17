// Like buttons (<form data-like data-like-url data-unlike-url>) toggle in place instead of reloading the
// page. Without this script the form still works: it posts and the server redirects back to the beep.
// When the background request fails (rate limit, signed out…) the form is submitted normally, so the
// page that comes back explains what happened. The live script (live.ts) also calls showLiked when the
// same person likes the beep in another tab.

const FETCH_HEADER = 'fetch';

/**
 * Shows the beep as liked or not. The count moves with the button only when the button actually changes, so a
 * like this tab made and the live event telling it about the same like count once; the next counts event has
 * the exact number anyway.
 */
export function showLiked(form: HTMLFormElement, liked: boolean): void {
  const button = form.querySelector<HTMLButtonElement>('button[aria-pressed]');
  if (!button) return;
  if ((button.getAttribute('aria-pressed') === 'true') !== liked) {
    button.setAttribute('aria-pressed', String(liked));
    const count = form.querySelector<HTMLElement>('[data-like-count]');
    if (count) count.textContent = String(Math.max(0, Number(count.textContent) + (liked ? 1 : -1)));
  }
  const next = liked ? form.dataset.unlikeUrl : form.dataset.likeUrl;
  if (next) form.action = next;
}

export function initLike(): void {
  document.addEventListener('submit', (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || !form.matches('[data-like]') || event.defaultPrevented) return;
    const button = form.querySelector<HTMLButtonElement>('button[aria-pressed]');
    if (!button || button.disabled) return;
    event.preventDefault();
    // Decided now: by the time the answer comes, a live event may already have pressed the button.
    const liked = button.getAttribute('aria-pressed') !== 'true';
    button.disabled = true;
    fetch(form.action, {
      method: 'POST',
      body: new FormData(form),
      headers: { 'X-Requested-With': FETCH_HEADER },
      credentials: 'same-origin',
    })
      .then((response) => {
        if (!response.ok) throw new Error(String(response.status));
        showLiked(form, liked);
      })
      .catch(() => form.submit())
      .finally(() => {
        button.disabled = false;
      });
  });
}
