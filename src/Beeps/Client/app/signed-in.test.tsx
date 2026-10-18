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

test('one\'s own beep can be deleted, after asking, and someone else\'s reported', async () => {
  const html = await draw('home.json', 'index');
  // 0d is the member's own (under review), the rest are alice's.
  assert.match(html, /00000000000d\/actions\?returnUrl=%2F&amp;handler=Delete" data-confirm="[^"]+"/);
  assert.match(html, /href="\/beeps\/00000000-0000-7000-8000-00000000000a\/report"/);
  assert.doesNotMatch(html, /00000000000d\/report/);
  assert.match(html, /<span class="badge badge-warning" title="[^"]+">Under review<\/span>/);
});

test('a profile has a follow button that follows the state', async () => {
  const followed = await draw('profile.json', 'profile');
  // tom follows alice in the sample.
  assert.match(followed, /<form method="post" action="\/alice\/follow\?handler=Unfollow">/);
  const notFollowed = await draw('profile.json', 'profile', (props) => {
    props.profile.following = false;
  });
  assert.match(notFollowed, /action="\/alice\/follow\?handler=Follow">\s*<button class="btn btn-sm btn-primary"/);
});

test('a blocked profile offers unblocking and no following', async () => {
  const html = await draw('profile.json', 'profile', (props) => {
    props.profile.blocked = true;
  });
  assert.match(html, /<form method="post" action="\/alice\/block\?handler=Unblock">/);
  assert.match(html, /You blocked @alice\./);
  assert.doesNotMatch(html, /\/alice\/follow\?handler=/);
  const open = await draw('profile.json', 'profile', (props) => {
    props.profile.blocked = false;
  });
  assert.match(open, /action="\/alice\/block\?handler=Block" data-confirm="[^"]+"/);
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
  // The account shows what it is and leads on to the password, a step below it.
  const account = await draw('settings-account.json', 'settings');
  assert.match(account, /<a class="column-back" href="\/settings"/);
  assert.match(account, /tom@example.com/);
  assert.match(account, /<a class="settings-link" href="\/settings\/password">/);
  assert.doesNotMatch(account, /<form/);
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
