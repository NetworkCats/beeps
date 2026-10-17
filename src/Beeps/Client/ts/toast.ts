// Toasts (<div data-toast>): what the last action did, at the bottom of the page. A notice goes by itself after a
// while; an alert (data-toast-sticky) stays until its close button is pressed.
import { PAGE_SWAPPED } from './page-swap.ts';

const NOTICE_TIME = 5000;
// Longer than the leaving animation, in case it never runs (reduced motion, a hidden tab).
const LEAVE_TIME = 400;

function dismiss(toast: HTMLElement): void {
  if (toast.classList.contains('toast-leaving')) return;
  toast.classList.add('toast-leaving');
  const remove = (): void => {
    const stack = toast.parentElement;
    toast.remove();
    if (stack?.matches('.toast-stack') && stack.childElementCount === 0) stack.remove();
  };
  toast.addEventListener('animationend', remove, { once: true });
  window.setTimeout(remove, LEAVE_TIME);
}

function schedule(): void {
  for (const toast of document.querySelectorAll<HTMLElement>('[data-toast]:not([data-toast-sticky]):not([data-toast-timed])')) {
    toast.dataset.toastTimed = '';
    window.setTimeout(() => dismiss(toast), NOTICE_TIME);
  }
}

/** Shows a notice of the page's own (a link copied), as the server shows one after a form. */
export function showToast(message: string): void {
  let stack = document.querySelector<HTMLElement>('.toast-stack');
  if (!stack) {
    stack = Object.assign(document.createElement('div'), { className: 'toast-stack' });
    stack.dataset.position = 'bottom-center';
    document.body.append(stack);
  }
  const toast = Object.assign(document.createElement('div'), { className: 'toast toast-dark' });
  toast.setAttribute('role', 'status');
  toast.dataset.toast = '';
  const icon = Object.assign(document.createElement('span'), { className: 'icon toast-icon', textContent: 'check_circle' });
  icon.setAttribute('aria-hidden', 'true');
  const content = Object.assign(document.createElement('div'), { className: 'toast-content' });
  content.append(Object.assign(document.createElement('p'), { className: 'toast-message', textContent: message }));
  toast.append(icon, content);
  stack.append(toast);
  schedule();
}

export function initToasts(): void {
  document.addEventListener('click', (event) => {
    const button = event.target instanceof Element ? event.target.closest('[data-toast-close]') : null;
    const toast = button?.closest<HTMLElement>('[data-toast]');
    if (toast) dismiss(toast);
  });
  schedule();
  document.addEventListener(PAGE_SWAPPED, schedule);
}
