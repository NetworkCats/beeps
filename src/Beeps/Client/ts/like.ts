// Like buttons (<form data-like data-like-url data-unlike-url>) toggle in place instead of reloading the
// page. Without this script the form still works: it posts and the server redirects back to the beep.
// When the background request fails (rate limit, signed out…) the form is submitted normally, so the
// page that comes back explains what happened.

const FETCH_HEADER = 'fetch';

function toggle(form: HTMLFormElement, button: HTMLButtonElement): void {
  const liked = button.getAttribute('aria-pressed') !== 'true';
  button.setAttribute('aria-pressed', String(liked));
  const count = form.querySelector<HTMLElement>('[data-like-count]');
  if (count) count.textContent = String(Math.max(0, Number(count.textContent) + (liked ? 1 : -1)));
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
    button.disabled = true;
    fetch(form.action, {
      method: 'POST',
      body: new FormData(form),
      headers: { 'X-Requested-With': FETCH_HEADER },
      credentials: 'same-origin',
    })
      .then((response) => {
        if (!response.ok) throw new Error(String(response.status));
        toggle(form, button);
      })
      .catch(() => form.submit())
      .finally(() => {
        button.disabled = false;
      });
  });
}
