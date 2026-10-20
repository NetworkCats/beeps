// The confirmation asked before a destructive action (dialogs.ts), in the layout's <dialog>.
import { useEffect, useRef } from 'preact/hooks';
import { confirmDialogReady, pendingConfirm } from '../dialogs.ts';
import { t } from '../i18n.ts';
import { PAGE_SHOWN } from '../router.ts';

export function ConfirmDialog() {
  const dialog = useRef<HTMLDialogElement>(null);
  const pending = pendingConfirm.value;

  useEffect(() => {
    confirmDialogReady.value = typeof dialog.current?.showModal === 'function';
    // Asked on the page left (Back while the dialog was open): confirming must not act on a page no longer shown.
    const cancel = (): void => {
      pendingConfirm.value?.answer(false);
      pendingConfirm.value = null;
      if (dialog.current?.open) dialog.current.close('cancel');
    };
    document.addEventListener(PAGE_SHOWN, cancel);
    return () => {
      confirmDialogReady.value = false;
      document.removeEventListener(PAGE_SHOWN, cancel);
      cancel();
    };
  }, []);

  useEffect(() => {
    const element = dialog.current;
    if (!element || !pending) return;
    element.returnValue = '';
    const closed = (): void => {
      pending.answer(element.returnValue === 'confirm');
      if (pendingConfirm.value === pending) pendingConfirm.value = null;
    };
    element.addEventListener('close', closed, { once: true });
    element.showModal();
    return () => element.removeEventListener('close', closed);
  }, [pending]);

  return (
    <dialog
      class="dialog dialog-sm dialog-plain confirm-dialog"
      aria-labelledby="confirm-message"
      ref={dialog}
      // A click on the backdrop (the dialog itself, outside its content) cancels, like Escape.
      onClick={(event) => {
        if (event.target === event.currentTarget) event.currentTarget.close('cancel');
      }}
    >
      <form method="dialog">
        <div class="dialog-body">
          <p class="confirm-message" id="confirm-message">
            {pending?.message ?? ''}
          </p>
        </div>
        <div class="dialog-footer">
          <button class="btn" value="cancel" autofocus>
            {t('Confirm.Cancel')}
          </button>
          <button class="btn btn-danger" value="confirm">
            {t('Confirm.Ok')}
          </button>
        </div>
      </form>
    </dialog>
  );
}
