// Forms drawn by the islands, sent in the background. The answer is the props of the page the form leads to, after
// its redirect: when that is this page, its islands just catch up (and say what was done); when the form was refused,
// the errors go next to it; anything else is shown as a page.
import type { TargetedSubmitEvent } from 'preact';
import { markSending } from './busy.ts';
import { confirmAction } from './dialogs.ts';
import { postIslands, type IslandsAnswer } from './http.ts';
import { showAnswer } from './page.ts';
import type { Errors } from './props.ts';
import { submitPage, visit } from './router.ts';
import { showToast } from './toasts.ts';
import { t } from './i18n.ts';

export interface SendOptions {
  /** The island whose page answers when the form is refused (a composer posts to the compose page): its errors are this form's. */
  answeredBy?: string;
  /** The form's errors, for drawing them next to it; empty once it went through. */
  onErrors?: (errors: Errors) => void;
  /** It went through and this page caught up. */
  onDone?: () => void;
}

function samePage(url: URL): boolean {
  return url.pathname === window.location.pathname;
}

export async function sendForm(form: HTMLFormElement, submitter: HTMLElement | null, options: SendOptions = {}): Promise<void> {
  const button = submitter instanceof HTMLButtonElement || submitter instanceof HTMLInputElement ? submitter : null;
  // A form with a button is sent once at a time: Enter or Ctrl+Enter again while it is on its way would post it twice.
  // One sent as soon as something in it changes (a switch) has none, and is sent each time, its latest answer standing.
  if (form.getAttribute('aria-busy') === 'true' && form.querySelector('button[type="submit"], button:not([type]), input[type="submit"]')) return;
  if (form.dataset.confirm && !(await confirmAction(form.dataset.confirm))) return;
  const action = button?.getAttribute('formaction') ? button.formAction : form.action;
  const data = new FormData(form, button);
  const sent = markSending(form, button);
  let answer: IslandsAnswer;
  try {
    answer = await postIslands(action, data);
  } catch {
    showToast(t('Error.Offline'), true);
    return;
  } finally {
    sent();
  }
  if (showAnswer(answer, samePage)) {
    options.onErrors?.(answer.page?.islands[options.answeredBy ?? '']?.errors ?? {});
    if (answer.status < 400) options.onDone?.();
    return;
  }
  // Refused, and answered by the page the form posts to: its errors belong next to the form here.
  const own = options.answeredBy ? answer.page?.islands[options.answeredBy] : undefined;
  if (answer.status >= 400 && own?.errors && options.onErrors) {
    options.onErrors(own.errors);
    return;
  }
  if (answer.status < 400 && answer.url) {
    options.onDone?.();
    // What the form did was said on the way (the redirect's notice): it shows over the page it led to.
    if (answer.page?.notice) showToast(answer.page.notice);
    if (answer.page?.alert) showToast(answer.page.alert, true);
    await visit(answer.url, true).catch(() => {});
    return;
  }
  // It went through, but the page it led to failed to load: sending it again would do it twice.
  if (answer.redirected) {
    await visit(answer.url).catch(() => {});
    return;
  }
  // Anything else (an error page): sent again the plain way, which nothing came of the first time, to show what went wrong.
  await submitPage(action, data);
}

/** An onSubmit that sends the form in the background; `done` once it went through. */
export function sending(done?: (form: HTMLFormElement) => void) {
  return (event: TargetedSubmitEvent<HTMLFormElement>): void => {
    event.preventDefault();
    const form = event.currentTarget;
    void sendForm(form, event.submitter ?? null, { onDone: () => done?.(form) });
  };
}
