// Cloudflare Turnstile's widget on the forms that need it (Pages/Shared/_Captcha.cshtml). Turnstile's own script draws
// it and adds the cf-turnstile-response field to the form; it is loaded once, when a page first has the widget, and
// asked to draw each widget the app shows (pages arrive in place, after the script has looked for widgets).
import { useEffect, useRef } from 'preact/hooks';
import type { IslandProps } from '../islands.tsx';

const SCRIPT = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';

interface Turnstile {
  render: (container: HTMLElement) => string;
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

export function Captcha({ props }: IslandProps<{ siteKey: string; language: string }>) {
  const container = useRef<HTMLDivElement>(null);
  useEffect(() => {
    let widget: string | undefined;
    let gone = false;
    turnstile().then(
      (api) => {
        if (!gone && container.current) widget = api.render(container.current);
      },
      () => {
        // Without the widget the server refuses the form, and says why.
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
