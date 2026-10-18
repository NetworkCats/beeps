// Cloudflare Turnstile's widget on the forms that need it (Pages/Shared/_Captcha.cshtml). Turnstile's own script draws
// it and adds the cf-turnstile-response field to the form; it is loaded once, when a page first has the widget, and
// asked to draw each widget the app shows (pages arrive in place, after the script has looked for widgets). The form's
// submit buttons take no clicks until the widget has passed (the server draws them disabled, for before this loads).
import { useEffect, useRef } from 'preact/hooks';
import type { IslandProps } from '../islands.tsx';

const SCRIPT = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';

interface TurnstileOptions {
  callback?: () => void;
  'expired-callback'?: () => void;
  'error-callback'?: () => void;
  'timeout-callback'?: () => void;
}

interface Turnstile {
  render: (container: HTMLElement, options?: TurnstileOptions) => string;
  remove: (widget: string) => void;
}

declare global {
  interface Window {
    turnstile?: Turnstile;
  }
}

let loading: Promise<Turnstile> | undefined;

function turnstile(): Promise<Turnstile> {
  loading ??= new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = SCRIPT;
    script.async = true;
    script.addEventListener('load', () => (window.turnstile ? resolve(window.turnstile) : reject(new Error('turnstile'))));
    script.addEventListener('error', () => {
      loading = undefined;
      reject(new Error('turnstile'));
    });
    document.head.append(script);
  });
  return loading;
}

/** Lets the form around the widget be sent, or not. */
function allowSending(container: HTMLElement, allowed: boolean): void {
  const buttons = container.closest('form')?.querySelectorAll<HTMLButtonElement>('button[type="submit"], button:not([type])') ?? [];
  for (const button of buttons) button.disabled = !allowed;
}

export function Captcha({ props }: IslandProps<{ siteKey: string; language: string }>) {
  const container = useRef<HTMLDivElement>(null);
  useEffect(() => {
    let widget: string | undefined;
    let gone = false;
    const element = container.current;
    if (!element) return;
    // A page brought back from history has its buttons as they were, but the widget is drawn afresh.
    allowSending(element, false);
    const block = () => allowSending(element, false);
    turnstile().then(
      (api) => {
        if (gone) return;
        widget = api.render(element, {
          callback: () => allowSending(element, true),
          'expired-callback': block,
          'error-callback': block,
          'timeout-callback': block,
        });
      },
      () => {
        // Without the widget the server refuses the form, and says why; better than a button that never wakes.
        if (!gone) allowSending(element, true);
      },
    );
    return () => {
      gone = true;
      if (widget) window.turnstile?.remove(widget);
    };
  }, []);
  return (
    <div class="field">
      <div class="cf-turnstile captcha" data-sitekey={props.siteKey} data-language={props.language} data-theme="auto" ref={container} />
    </div>
  );
}
