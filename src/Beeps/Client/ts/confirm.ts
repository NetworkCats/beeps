// <form data-confirm="Are you sure?">: asks before submitting a destructive action.
export function initConfirm(): void {
  document.addEventListener('submit', (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement)) return;
    const message = form.dataset.confirm;
    if (message && !window.confirm(message)) event.preventDefault();
  });
}
