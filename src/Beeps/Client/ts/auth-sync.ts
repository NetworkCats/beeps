// Tabs of one browser share the sign-in, so when one signs in, out, or finishes verifying, the others follow at once,
// signed-out pages included. This is no live connection (PLAN 6.10 keeps those to members): the tabs tell each other
// over a BroadcastChannel, which never leaves the browser, and a tab that is behind fetches its own page again.
// <body data-auth="member:{id}" | "unverified:{id}">; signed-out pages have none, so cached pages stay the same for all.

import { PAGE_SWAPPED, swapPage } from './page-swap.ts';

const CHANNEL = 'beeps-auth';
const RETRY_DELAY = 5000;

function authState(): string {
  return document.body.dataset.auth ?? 'anonymous';
}

async function catchUp(): Promise<void> {
  try {
    await swapPage();
  } catch {
    window.setTimeout(() => void catchUp(), RETRY_DELAY);
  }
}

export function initAuthSync(): void {
  if (!('BroadcastChannel' in window)) return;
  const channel = new BroadcastChannel(CHANNEL);
  channel.addEventListener('message', (event: MessageEvent<unknown>) => {
    if (typeof event.data === 'string' && event.data !== authState()) void catchUp();
  });
  // Every page says how it was signed in when it opens or changes; the others compare with theirs.
  channel.postMessage(authState());
  document.addEventListener(PAGE_SWAPPED, () => channel.postMessage(authState()));
}
