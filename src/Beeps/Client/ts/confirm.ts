// <form data-confirm="Are you sure?">: asks before submitting a destructive action, in the layout's
// <dialog data-confirm-dialog>, and submits the form again once confirmed. Without that dialog (or without
// <dialog> support) the browser's own confirm() asks instead.

const confirmed = new WeakSet<HTMLFormElement>();

function submitAgain(form: HTMLFormElement, submitter: HTMLElement | null): void {
  confirmed.add(form);
  form.requestSubmit(submitter instanceof HTMLButtonElement && submitter.form === form ? submitter : undefined);
}

export function initConfirm(): void {
  document.addEventListener('submit', (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement)) return;
    const message = form.dataset.confirm;
    if (!message) return;
    if (confirmed.delete(form)) return;
    event.preventDefault();
    const submitter = (event as SubmitEvent).submitter;
    const dialog = document.querySelector<HTMLDialogElement>('dialog[data-confirm-dialog]');
    if (!dialog || typeof dialog.showModal !== 'function') {
      if (window.confirm(message)) submitAgain(form, submitter);
      return;
    }
    const text = dialog.querySelector('[data-confirm-message]');
    if (text) text.textContent = message;
    // The menu the action came from closes, as it would after picking anything in it.
    form.closest('details[open]')?.removeAttribute('open');
    dialog.returnValue = '';
    dialog.addEventListener(
      'close',
      () => {
        if (dialog.returnValue === 'confirm') submitAgain(form, submitter);
      },
      { once: true },
    );
    dialog.showModal();
  });
  // A click on the backdrop (the dialog itself, outside its content) cancels, like Escape.
  document.addEventListener('click', (event) => {
    if (event.target instanceof HTMLDialogElement && event.target.matches('[data-confirm-dialog]')) event.target.close('cancel');
  });
}
