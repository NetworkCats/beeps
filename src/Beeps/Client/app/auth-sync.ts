// Tabs of one browser share the sign-in, so when one signs in, out, or finishes verifying, the others follow at once,
// signed-out pages included. This is no live connection (PLAN 6.10 keeps those to members): the tabs tell each other
// over a BroadcastChannel, which never leaves the browser, and a tab that is behind loads its page again.
// <body data-auth="member:{id}" | "unverified:{id}">; signed-out pages have none, so cached pages stay the same for all.

const CHANNEL = 'beeps-auth';

function authState(): string {
  return document.body.dataset.auth ?? 'anonymous';
}

/** Says how this page is signed in; `catchUp` runs when another tab of the browser is signed in differently. */
export function initAuthSync(catchUp: () => void): void {
  if (!('BroadcastChannel' in window)) return;
  const channel = new BroadcastChannel(CHANNEL);
  channel.addEventListener('message', (event: MessageEvent<unknown>) => {
    if (typeof event.data === 'string' && event.data !== authState()) catchUp();
  });
  channel.postMessage(authState());
}
