// Toasts: what the last action did, at the bottom of the page (shell/Toasts.tsx). A notice goes by itself after a
// while; an alert stays until it is closed.
import { signal } from '@preact/signals';

interface Toast {
  id: number;
  message: string;
  alert: boolean;
  leaving?: boolean;
  /** It has risen in: each page draws the toasts anew, and one already shown does not rise in again. */
  settled?: boolean;
}

export const toasts = signal<Toast[]>([]);

const NOTICE_TIME = 5000;
// Longer than the leaving animation, in case it never runs (reduced motion, a hidden tab).
const LEAVE_TIME = 400;
let nextId = 1;

export function showToast(message: string, alert = false): void {
  const toast: Toast = { id: nextId++, message, alert };
  toasts.value = [...toasts.value, toast];
  if (!alert) window.setTimeout(() => dismissToast(toast.id), NOTICE_TIME);
}

export function dismissToast(id: number): void {
  if (!toasts.value.some((toast) => toast.id === id && !toast.leaving)) return;
  toasts.value = toasts.value.map((toast) => (toast.id === id ? { ...toast, leaving: true } : toast));
  window.setTimeout(() => removeToast(id), LEAVE_TIME);
}

export function removeToast(id: number): void {
  toasts.value = toasts.value.filter((toast) => toast.id !== id);
}
