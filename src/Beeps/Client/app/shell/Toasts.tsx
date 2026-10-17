// The toasts at the bottom of the page (toasts.ts): what the server said after the last form, and what the app says.
import { useState } from 'preact/hooks';
import { t } from '../i18n.ts';
import type { IslandProps } from '../islands.tsx';
import { useSnapshot } from '../islands.tsx';
import { dismissToast, removeToast, showToast, toasts } from '../toasts.ts';
import { Icon } from '../components/Icon.tsx';

export function Toasts({ props }: IslandProps<{ notice?: string; alert?: string }>) {
  // The page's own notices join the toasts once, as it is shown; gone back to, it has none.
  useState(() => {
    if (props.notice) showToast(props.notice);
    if (props.alert) showToast(props.alert, true);
    return true;
  });
  useSnapshot(() => ({}));
  const list = toasts.value;
  if (list.length === 0) return null;
  return (
    <div class="toast-stack" data-position="bottom-center">
      {list.map((toast) => (
        <div
          key={toast.id}
          class={`toast ${toast.alert ? 'toast-danger' : 'toast-dark'}${toast.leaving ? ' toast-leaving' : ''}`}
          role={toast.alert ? 'alert' : 'status'}
         
         
          onAnimationEnd={() => {
            if (toast.leaving) removeToast(toast.id);
          }}
        >
          {toast.alert ? <Icon name="error" class="toast-icon" /> : <Icon name="check_circle" class="toast-icon" />}
          <div class="toast-content">
            <p class="toast-message">{toast.message}</p>
          </div>
          <button class="btn-close toast-close" type="button" aria-label={t('Toast.Close')} onClick={() => dismissToast(toast.id)} />
        </div>
      ))}
    </div>
  );
}
