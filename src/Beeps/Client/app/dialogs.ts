// The confirmation asked before a destructive action, in the layout's dialog (shell/ConfirmDialog.tsx). Without that
// dialog (or without <dialog> support) the browser's own confirm() asks instead.
import { signal } from '@preact/signals';

export interface PendingConfirm {
  message: string;
  answer: (confirmed: boolean) => void;
}

export const pendingConfirm = signal<PendingConfirm | null>(null);

/** Whether a confirmation dialog is on the page to ask with. */
export const confirmDialogReady = { value: false };

/** Asks before something destructive is done. */
export function confirmAction(message: string): Promise<boolean> {
  if (!confirmDialogReady.value) return Promise.resolve(window.confirm(message));
  pendingConfirm.value?.answer(false);
  return new Promise((resolve) => {
    pendingConfirm.value = { message, answer: resolve };
  });
}
