// Signed in, what members see is drawn by the components alone (the server sends props only), so what the server's
// page tests once checked in that markup is checked here: the components drawn from the same samples the server's
// tests keep (tests/Beeps.Tests/Pages/IslandSamples), as a member would get them.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { renderToString } from 'preact-render-to-string';
import { setConfig, type AppConfig } from './config.ts';
import { setStrings } from './i18n.ts';
import { loadComponent } from './islands.tsx';
import { registerPages } from './pages/index.ts';
import type { Errors } from './props.ts';
import { registerShell } from './shell/index.ts';

interface Sample {
  url: string;
  config: AppConfig;
  strings: Record<string, string>;
  islands: Array<{ name: string; props: unknown; errors?: Errors | null }>;
}

registerShell();
registerPages();

const samples = resolve('tests/Beeps.Tests/Pages/IslandSamples');

function load(file: string): Sample {
  return JSON.parse(readFileSync(resolve(samples, file), 'utf8')) as Sample;
}

/** The island drawn as the member of the sample sees it; `change` adjusts its props first. */
async function draw(file: string, name: string, change: (props: Record<string, any>) => void = () => {}): Promise<string> {
  const sample = load(file);
  Object.assign(globalThis, {
    window: { location: new URL(sample.url, 'https://beeps.test'), setTimeout, clearTimeout },
    document: { documentElement: { lang: 'en', dataset: {} } },
  });
  setConfig(sample.config);
  setStrings(sample.config.stringsUrl, sample.strings);
  const island = sample.islands.find((i) => i.name === name);
  assert.ok(island, `no island ${name} in ${file}`);
  const props = structuredClone(island.props) as Record<string, any>;
  change(props);
  const Component = await loadComponent(name);
  assert.ok(Component, `no component for ${name}`);
  return renderToString(<Component props={props} errors={island.errors ?? {}} />);
}

test('a liked beep shows the like pressed and posts the unlike', async () => {
  const html = await draw('home.json', 'index');
  // Beep 0b is liked in the sample, 0a is not.
  assert.match(html, /actions\?returnUrl=%2F&amp;handler=Unlike"[^>]*>\s*<button class="beep-action beep-action-like" type="submit" aria-pressed="true"/);
  assert.match(html, /00000000000a\/actions\?returnUrl=%2F&amp;handler=Like"/);
});

test('one\'s own beep can be deleted, and someone else\'s reported', async () => {
  const html = await draw('home.json', 'index');
  // 0d is the member's own (under review), the rest are alice's.
  assert.match(html, /00000000000d\/actions\?returnUrl=%2F&amp;handler=Delete"/);
  assert.match(html, /href="\/beeps\/00000000-0000-7000-8000-00000000000a\/report"/);
  assert.doesNotMatch(html, /00000000000d\/report/);
  assert.match(html, /<span class="badge badge-warning" title="[^"]+">Under review<\/span>/);
});

test('a profile has a follow button that follows the state', async () => {
  const followed = await draw('profile.json', 'profile');
  // tom follows alice in the sample.
  assert.match(followed, /<form method="post" action="\/@alice\/follow\?handler=Unfollow">/);
  const notFollowed = await draw('profile.json', 'profile', (props) => {
    props.profile.following = false;
  });
  assert.match(notFollowed, /action="\/@alice\/follow\?handler=Follow">\s*<button class="btn btn-sm btn-primary"/);
});

test('a human with more than three agents lists three, and all of them in the dialog "show more" opens', async () => {
  const html = await draw('profile.json', 'profile');
  const [card = '', dialog = ''] = html.split('<dialog');
  assert.equal(card.match(/class="actor-row"/g)?.length, 3);
  assert.equal(dialog.match(/class="actor-row"/g)?.length, 4);
  assert.match(card, /<button class="profile-more" type="button" commandfor="agents-dialog" command="show-modal">Show more<\/button>/);
  const few = await draw('profile.json', 'profile', (props) => {
    props.profile.agents = props.profile.agents.slice(0, 3);
  });
  assert.doesNotMatch(few, /<dialog|profile-more/);
});

test('a blocked profile offers unblocking and no following', async () => {
  const html = await draw('profile.json', 'profile', (props) => {
    props.profile.blocked = true;
  });
  assert.match(html, /<form method="post" action="\/@alice\/block\?handler=Unblock">/);
  assert.match(html, /You blocked @alice\./);
  assert.doesNotMatch(html, /\/@alice\/follow\?handler=/);
  const open = await draw('profile.json', 'profile', (props) => {
    props.profile.blocked = false;
  });
  assert.match(open, /action="\/@alice\/block\?handler=Block" data-confirm="[^"]+"/);
});

test('a reply in a thread says whom it answers, with a link to that beep on the page', async () => {
  const html = await draw('thread.json', 'thread');
  assert.match(html, /<p class="beep-context"><a href="#beep-00000000-0000-7000-8000-00000000000a">Replying to @alice<\/a><\/p>/);
  assert.match(html, /<article class="beep beep-focused" id="beep-00000000-0000-7000-8000-00000000000b"/);
  assert.match(html, /<form class="composer" method="post" enctype="multipart\/form-data" action="\/compose\?replyTo=00000000-0000-7000-8000-00000000000b" id="reply">/);
});

test('the admin overview marks a review queue that waits for attention', async () => {
  const waiting = await draw('admin-overview.json', 'admin-overview');
  assert.match(waiting, /<div class="stat stat-attention"><dt><a href="\/admin\/review">/);
  const empty = await draw('admin-overview.json', 'admin-overview', (props) => {
    props.reviewQueue = 0;
  });
  assert.doesNotMatch(empty, /stat-attention"><dt><a href="\/admin\/review">/);
});

test('a new invite\'s code is shown once, in an alert with a copy button', async () => {
  const html = await draw('admin-settings.json', 'admin-settings', (props) => {
    props.newInvite = { id: props.invites[0].id, code: 'ABCD-EFGH-JKLM-NPQR' };
  });
  assert.match(html, /<div class="alert alert-success"/);
  assert.match(html, /<code>ABCD-EFGH-JKLM-NPQR<\/code>/);
  assert.match(html, /<button class="btn-copy" type="button"/);
});

test('the settings list the sections, each a page of its own with its forms', async () => {
  const list = await draw('settings.json', 'settings');
  assert.match(list, /<a class="settings-link" href="\/settings\/profile">/);
  assert.match(list, /<a class="settings-link" href="\/settings\/account">/);
  assert.match(list, /<a class="settings-link" href="\/settings\/display">/);
  assert.doesNotMatch(list, /<form/);
  // The account shows what it is and leads on to the email and the password, a step below it; then Google and GitHub,
  // linked by going out to them, the plain way.
  const account = await draw('settings-account.json', 'settings');
  assert.match(account, /<a class="column-back" href="\/settings"/);
  assert.match(account, /<a class="settings-link" href="\/settings\/email">/);
  assert.match(account, /tom@example.com/);
  assert.match(account, /<a class="settings-link" href="\/settings\/password">/);
  assert.match(account, /<form method="post" action="\/oauth\/google\?handler=Link">/);
  assert.match(account, /<form method="post" action="\/oauth\/github\?handler=Link">/);
  assert.doesNotMatch(account, /handler=Unlink/);
  // Signed up with GitHub: it is unlinked here, after asking, and a password is set with none to give first.
  const linked = await draw('settings-linked-account.json', 'settings');
  assert.match(linked, /<span class="settings-link-help">octocat<\/span>/);
  assert.match(linked, /<form method="post" action="\/settings\/account\?handler=Unlink" data-confirm="[^"]*GitHub[^"]*">/);
  assert.match(linked, /<input type="hidden" name="Provider" value="github"\/>/);
  assert.match(linked, /action="\/oauth\/google\?handler=Link"/);
  assert.match(linked, /Set a password/);
  const setPassword = await draw('settings-linked-password.json', 'settings');
  assert.match(setPassword, /<h1 class="column-title">Set a password<\/h1>/);
  assert.match(setPassword, /name="NewPassword"/);
  assert.doesNotMatch(setPassword, /name="CurrentPassword"/);
  // The email: the new address and the password first, then the code sent there.
  const email = await draw('settings-email.json', 'settings');
  assert.match(email, /<a class="column-back" href="\/settings\/account"/);
  assert.match(email, /action="\/settings\/email\?handler=Email"/);
  assert.match(email, /name="NewEmail"/);
  assert.match(email, /name="CurrentPassword"/);
  const code = await draw('settings-email.json', 'settings', (props) => {
    props.pendingEmail = 'new@example.org';
  });
  assert.match(code, /new@example.org/);
  assert.match(code, /action="\/settings\/email\?handler=ConfirmEmail"/);
  assert.match(code, /name="Code"/);
  assert.match(code, /action="\/settings\/email\?handler=CancelEmail"/);
  assert.doesNotMatch(code, /name="NewEmail"/);
  // A new code once the minute since the last one is up; until then the button counts down.
  assert.match(code, /action="\/settings\/email\?handler=ResendEmail"/);
  assert.match(code, /<button class="btn btn-ghost btn-sm" type="submit">Send a new code<\/button>/);
  const waiting = await draw('settings-email.json', 'settings', (props) => {
    props.pendingEmail = 'new@example.org';
    props.resendAt = new Date(Date.now() + 42_000).toISOString();
  });
  assert.match(waiting, /<button class="btn btn-ghost btn-sm" type="submit" disabled>Send a new code in 42 s<\/button>/);
  const password = await draw('settings-password.json', 'settings');
  assert.match(password, /<a class="column-back" href="\/settings\/account"/);
  assert.match(password, /action="\/settings\/password\?handler=Password"/);
  // Profile: the images' own forms, and the name and bio saved from the header.
  const profile = await draw('settings-profile.json', 'settings');
  assert.match(profile, /action="\/settings\/profile\?handler=Banner"/);
  assert.match(profile, /action="\/settings\/profile\?handler=Avatar"/);
  assert.match(profile, /<form class="settings-form" id="profile-form" method="post" action="\/settings\/profile\?handler=Profile"/);
  assert.match(profile, /<button class="btn btn-sm btn-primary column-header-end" type="submit" form="profile-form">/);
  // Display: a scheme is applied once picked, with no button of its own.
  const display = await draw('settings-display.json', 'settings');
  assert.match(display, /<input type="radio" name="Theme" value="dark"/);
  assert.match(display, /action="\/settings\/display\?handler=Language"/);
});

test('the verification page says where the code went, sends another after a minute, and takes another address', async () => {
  const page = await draw('verify.json', 'verify');
  assert.match(page, /ann@example.com/);
  assert.match(page, /action="\/onboarding\/verify"/);
  assert.match(page, /action="\/onboarding\/verify\?handler=Resend"/);
  assert.match(page, /<button class="btn btn-ghost btn-sm" type="submit">Send a new code<\/button>/);
  // The form for another address opens from a button; refused, it comes back open.
  assert.doesNotMatch(page, /name="NewEmail"/);
  const waiting = await draw('verify.json', 'verify', (props) => {
    props.resendAt = new Date(Date.now() + 59_500).toISOString();
  });
  assert.match(waiting, /disabled>Send a new code in 60 s<\/button>/);
  const changing = await draw('verify.json', 'verify', (props) => {
    props.changing = true;
  });
  assert.match(changing, /action="\/onboarding\/verify\?handler=Email"/);
  assert.match(changing, /name="NewEmail"/);
  assert.match(changing, /name="CurrentPassword"/);
});
