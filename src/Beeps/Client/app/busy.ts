// A form on its way to the server: the button it was sent with shows a spinner in place of its label (collection-ui's
// .btn-loading) and takes no more clicks until the answer is in. The page's loading bar (router.ts) shows the rest.

/** Marks the form and the button it was sent with (its first, when sent with Enter); returns what undoes it. */
export function markSending(form: HTMLFormElement, submitter: Element | null): () => void {
  form.setAttribute('aria-busy', 'true');
  const button = submitter ?? form.querySelector('button[type="submit"], button:not([type]), input[type="submit"]');
  const spinning = button instanceof HTMLElement && button.matches('.btn:not(.btn-loading)') ? button : null;
  spinning?.classList.add('btn-loading');
  return () => {
    form.removeAttribute('aria-busy');
    spinning?.classList.remove('btn-loading');
  };
}
