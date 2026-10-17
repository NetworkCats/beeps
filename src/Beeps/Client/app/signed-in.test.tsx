// Signed in, what members see is drawn by the components alone (the server sends props only), so what the server's
// page tests once checked in that markup is checked here: the components drawn from the same samples the server's
// tests keep (tests/Beeps.Tests/Pages/IslandSamples), as a member would get them.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { renderToString } from 'preact-render-to-string';
import { setConfig, type AppConfig } from './config.ts';
import { componentFor } from './islands.tsx';
import { registerPages } from './pages/index.ts';
import type { Errors } from './props.ts';
import { registerShell } from './shell/index.ts';

interface Sample {
  url: string;
  config: AppConfig;
  islands: Array<{ name: string; props: unknown; errors?: Errors | null }>;
}

registerShell();
registerPages();

const samples = resolve('tests/Beeps.Tests/Pages/IslandSamples');

function load(file: string): Sample {
  return JSON.parse(readFileSync(resolve(samples, file), 'utf8')) as Sample;
}

/** The island drawn as the member of the sample sees it; `change` adjusts its props first. */
function draw(file: string, name: string, change: (props: Record<string, any>) => void = () => {}): string {
  const sample = load(file);
  Object.assign(globalThis, {
    window: { location: new URL(sample.url, 'https://beeps.test'), setTimeout, clearTimeout },
    document: { documentElement: { lang: 'en', dataset: {} } },
  });
  setConfig(sample.config);
  const island = sample.islands.find((i) => i.name === name);
  assert.ok(island, `no island ${name} in ${file}`);
  const props = structuredClone(island.props) as Record<string, any>;
  change(props);
  const Component = componentFor(name);
  assert.ok(Component, `no component for ${name}`);
  return renderToString(<Component props={props} errors={island.errors ?? {}} />);
}

/** The opening tag of the first element with this class, attributes and all. */
function tag(html: string, className: string): string {
  const found = html.match(new RegExp(`<[a-z]+ [^>]*class="${className}"[^>]*>`));
  assert.ok(found, `no element with class "${className}"`);
  return found[0];
}

test('the home timeline has a composer that posts its text and images to the compose page', () => {
  const html = draw('home.json', 'index');
  const form = tag(html, 'composer');
  assert.match(form, /method="post"/);
  assert.match(form, /enctype="multipart\/form-data"/);
  assert.match(form, /action="\/compose"/);
  assert.match(html, /<input type="hidden" name="ReturnUrl" value="\/"\/>/);
  assert.match(html, /<input class="composer-file" type="file" name="Images" multiple/);
  assert.match(html, /name="__RequestVerificationToken"/);
});

test('a liked beep shows the like pressed and posts the unlike', () => {
  const html = draw('home.json', 'index');
  // Beep 0b is liked in the sample, 0a is not.
  assert.match(html, /actions\?returnUrl=%2F&amp;handler=Unlike"[^>]*>\s*<button class="beep-action beep-action-like" type="submit" aria-pressed="true"/);
  assert.match(html, /00000000000a\/actions\?returnUrl=%2F&amp;handler=Like"/);
});

test('one\'s own beep can be deleted, after asking, and someone else\'s reported', () => {
  const html = draw('home.json', 'index');
  // 0d is the member's own (under review), the rest are alice's.
  assert.match(html, /00000000000d\/actions\?returnUrl=%2F&amp;handler=Delete" data-confirm="[^"]+"/);
  assert.match(html, /href="\/beeps\/00000000-0000-7000-8000-00000000000a\/report"/);
  assert.doesNotMatch(html, /00000000000d\/report/);
  assert.match(html, /<span class="badge badge-warning" title="[^"]+">Under review<\/span>/);
});

test('the account menu works without script and leads to the profile, settings and signing out', () => {
  for (const [rail, menu] of [[true, 'menu sidenav-menu'], [false, 'menu topbar-menu']] as const) {
    const html = draw('home.json', 'account-menu', (props) => {
      props.rail = rail || undefined;
    });
    // A <details>: it opens without script.
    assert.match(html, new RegExp(`^<details class="${menu}"><summary class="menu-toggle" aria-label="Account menu">`));
    assert.match(html, /<a class="dropdown-item" href="\/tom">/);
    assert.match(html, /<a class="dropdown-item" href="\/settings">/);
    assert.match(html, /<form method="post" action="\/logout">/);
  }
});

test('a profile has its beeps and replies tabs and a follow button that follows the state', () => {
  const followed = draw('profile.json', 'profile');
  assert.match(followed, /<a class="tab" href="\/alice" aria-current="page">Beeps<\/a>/);
  assert.match(followed, /<a class="tab" href="\/alice\/replies">Replies<\/a>/);
  // tom follows alice in the sample.
  assert.match(followed, /<form method="post" action="\/alice\/follow\?handler=Unfollow">/);
  const notFollowed = draw('profile.json', 'profile', (props) => {
    props.profile.following = false;
  });
  assert.match(notFollowed, /action="\/alice\/follow\?handler=Follow">\s*<button class="btn btn-sm btn-primary"/);
});

test('a blocked profile offers unblocking and no following', () => {
  const html = draw('profile.json', 'profile', (props) => {
    props.profile.blocked = true;
  });
  assert.match(html, /<form method="post" action="\/alice\/block\?handler=Unblock">/);
  assert.match(html, /You blocked @alice\./);
  assert.doesNotMatch(html, /\/alice\/follow\?handler=/);
  const open = draw('profile.json', 'profile', (props) => {
    props.profile.blocked = false;
  });
  assert.match(open, /action="\/alice\/block\?handler=Block" data-confirm="[^"]+"/);
});

test('a reply in a thread says whom it answers, with a link to that beep on the page', () => {
  const html = draw('thread.json', 'thread');
  assert.match(html, /<p class="beep-context"><a href="#beep-00000000-0000-7000-8000-00000000000a">Replying to @alice<\/a><\/p>/);
  assert.match(html, /<article class="beep beep-focused" id="beep-00000000-0000-7000-8000-00000000000b"/);
  assert.match(html, /<form class="composer" method="post" enctype="multipart\/form-data" action="\/compose\?replyTo=00000000-0000-7000-8000-00000000000b" id="reply">/);
});

test('the admin overview marks a review queue that waits for attention', () => {
  const waiting = draw('admin-overview.json', 'admin-overview');
  assert.match(waiting, /<div class="stat stat-attention"><dt><a href="\/admin\/review">/);
  const empty = draw('admin-overview.json', 'admin-overview', (props) => {
    props.reviewQueue = 0;
  });
  assert.doesNotMatch(empty, /stat-attention"><dt><a href="\/admin\/review">/);
});

test('a new invite\'s code is shown once, in an alert with a copy button', () => {
  const html = draw('admin-settings.json', 'admin-settings', (props) => {
    props.newInvite = { id: props.invites[0].id, code: 'ABCD-EFGH-JKLM-NPQR' };
  });
  assert.match(html, /<div class="alert alert-success"/);
  assert.match(html, /<code>ABCD-EFGH-JKLM-NPQR<\/code>/);
  assert.match(html, /<button class="btn-copy" type="button"/);
});
