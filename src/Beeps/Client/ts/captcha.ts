// Page entry for forms with Cloudflare Turnstile (register, forgot password). Turnstile's own script
// renders every .cf-turnstile element and adds the cf-turnstile-response field to its form; it is
// loaded from here so pages never carry a third-party <script> tag of their own.
const TURNSTILE_SCRIPT = 'https://challenges.cloudflare.com/turnstile/v0/api.js';

if (document.querySelector('.cf-turnstile')) {
  const script = document.createElement('script');
  script.src = TURNSTILE_SCRIPT;
  script.async = true;
  script.defer = true;
  document.head.append(script);
}
