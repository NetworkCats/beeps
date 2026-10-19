// The account's own settings (Settings.cshtml), split as Twitter splits them: /settings lists the sections, and each
// has a page of its own, with a way back to the list. Profile is Twitter's "Edit profile" (the banner and avatar, the
// name and bio, saved from the header); privacy holds a switch for each thing the profile can keep to the account, sent
// as soon as it is flipped; account shows the username and leads to the email's, the password's and the passkeys' pages,
// the email moving only once the code sent to the new address is entered, then lists Google and GitHub to link (out to
// the provider and back, Pages/Account/External) or unlink; passkeys lists the account's, adds one (the server's options,
// then the browser's dialog) or removes any, and switches them between signing in by themselves and being the second
// step after the password; display holds the colour scheme, applied as
// soon as one is picked, and the language; devices lists where the account is logged in, this device first, each
// named from its User-Agent and placed where its IP address is, and logs out any other or all of them, following
// log-ins and log-outs elsewhere as they happen. Each form is sent in the background and
// answered in place; the saved name, bio, avatar and banner follow changes made in another tab, unless they are being
// edited here. A new language shows the page anew in it.
import type { ComponentChildren } from 'preact';
import { useRef, useState } from 'preact/hooks';
import { FloatField } from '../components/FloatField.tsx';
import { Antiforgery, FormErrors, field } from '../components/Forms.tsx';
import { Icon } from '../components/Icon.tsx';
import { CodeResend } from '../components/CodeResend.tsx';
import { Time } from '../components/Time.tsx';
import { ProfileImages, type AvatarEditorProps, type BannerEditorProps } from '../components/ProfileImageEditor.tsx';
import { sending, sendForm } from '../forms.ts';
import { formData } from '../http.ts';
import { t } from '../i18n.ts';
import type { IslandProps } from '../islands.tsx';
import { useLive } from '../page.ts';
import { createPasskey, PasskeyError, passkeysSupported } from '../passkeys.ts';
import { paths } from '../paths.ts';
import type { Errors, LiveProps } from '../props.ts';
import { theme } from '../state.ts';
import { showToast } from '../toasts.ts';

type Section = 'profile' | 'privacy' | 'account' | 'email' | 'password' | 'passkeys' | 'display' | 'sessions';

interface Option {
  value: string;
  label: string;
  lang?: string;
}

interface Privacy {
  hideLikes: boolean;
  hideFollows: boolean;
  hideFollowCounts: boolean;
}

/** A provider to sign in with: Google or GitHub. */
interface Connection {
  provider: string;
  label: string;
  /** How the linked account is known there; none when not linked. */
  name?: string;
}

/** A device logged in to the account. */
interface Device {
  id: string;
  kind: 'phone' | 'tablet' | 'computer' | 'unknown';
  /** The device or system, from its User-Agent; none when it does not say. */
  name?: string;
  browser?: string;
  /** The town or region, and the country, it logged in from, where known. */
  locality?: string;
  country?: string;
  signedInAt: string;
  lastSeenAt: string;
  /** The device this page is shown on. */
  current?: boolean;
}

/** A passkey of the account's, named after where it was added. */
interface Passkey {
  id: string;
  /** The device or system it was added on, from its User-Agent; none when it does not say. */
  name?: string;
  browser?: string;
  createdAt: string;
  /** None if it never signed in. */
  lastUsedAt?: string;
}

export interface ProfileLimits {
  displayNameMaxLength: number;
  bioMaxLength: number;
}

interface SettingsProps {
  /** None for the list of sections. */
  section?: Section;
  live: LiveProps;
  avatar: AvatarEditorProps;
  banner: BannerEditorProps;
  handle: string;
  email: string;
  /** The address a code was sent to, while it can still be entered. */
  pendingEmail?: string;
  /** When another code may be sent to `pendingEmail`. */
  resendAt?: string;
  displayName?: string;
  bio?: string;
  privacy: Privacy;
  culture: string;
  cultures: Option[];
  themes: Option[];
  limits: ProfileLimits;
  passwordMinLength: number;
  passwordMaxLength: number;
  codeLength: number;
  codeLifetimeMinutes: number;
  /** False for an account that signs in only with Google or GitHub: it sets a password, with none to give first. */
  hasPassword: boolean;
  connections: Connection[];
  /** The devices logged in, this one first; only on their page. */
  devices?: Device[];
  /** The passkeys are the second step after the password, not a way to sign in by themselves. */
  passkeyTwoFactor?: boolean;
  /** The account's passkeys, oldest first; only on their page. */
  passkeys?: Passkey[];
}

interface SectionProps {
  props: SettingsProps;
  errors: Errors;
}

/** Each section's title, and where its back arrow leads: the list, or the section it is a step below. */
const TITLES: Record<Section, { title: string; back: string }> = {
  profile: { title: 'Settings.Profile', back: paths.settings },
  privacy: { title: 'Settings.Privacy', back: paths.settings },
  account: { title: 'Settings.Account', back: paths.settings },
  email: { title: 'Settings.ChangeEmail', back: paths.settingsSection('account') },
  password: { title: 'Settings.ChangePassword', back: paths.settingsSection('account') },
  passkeys: { title: 'Settings.Passkeys', back: paths.settingsSection('account') },
  display: { title: 'Settings.Display', back: paths.settings },
  sessions: { title: 'Settings.Sessions', back: paths.settings },
};

/** Where a section's forms post: its own page, so a refused one comes back to it. */
function action(section: Section, handler: string): string {
  return `${paths.settingsSection(section)}?handler=${handler}`;
}

/** A row that leads to a page: its icon, title and what it holds, and a chevron. */
function LinkRow({ href, icon, title, help }: { href: string; icon: ComponentChildren; title: string; help: string }) {
  return (
    <a class="settings-link" href={href}>
      {icon}
      <span class="settings-link-text">
        <span class="settings-link-title">{title}</span>
        <span class="settings-link-help">{help}</span>
      </span>
      <Icon name="chevron_right" class="settings-link-chevron" />
    </a>
  );
}

export function SettingsPage({ props, errors }: IslandProps<SettingsProps>) {
  useLive(props.live);
  const section = props.section && TITLES[props.section] ? props.section : undefined;
  if (!section) {
    return (
      <section class="feed">
        <header class="column-header">
          <h1 class="column-title">{t('Settings.Title')}</h1>
        </header>
        <FormErrors errors={errors} />
        <nav class="settings-nav" aria-label={t('Settings.Title')}>
          <LinkRow href={paths.settingsSection('profile')} icon={<Icon name="person" />} title={t('Settings.Profile')} help={t('Settings.Profile.Help')} />
          <LinkRow href={paths.settingsSection('privacy')} icon={<Icon name="shield" />} title={t('Settings.Privacy')} help={t('Settings.Privacy.Help')} />
          <LinkRow href={paths.settingsSection('account')} icon={<Icon name="lock" />} title={t('Settings.Account')} help={t('Settings.Account.Help')} />
          <LinkRow href={paths.settingsSection('display')} icon={<Icon name="palette" />} title={t('Settings.Display')} help={t('Settings.Display.Help')} />
          <LinkRow href={paths.settingsSection('sessions')} icon={<Icon name="devices" />} title={t('Settings.Sessions')} help={t('Settings.Sessions.Help')} />
        </nav>
      </section>
    );
  }
  const { back } = TITLES[section];
  const title = section === 'password' && !props.hasPassword ? 'Settings.SetPassword' : TITLES[section].title;
  return (
    <section class="feed">
      <header class="column-header">
        <a class="column-back" href={back} aria-label={t('Nav.Back')}>
          <Icon name="arrow_back" />
        </a>
        <h1 class="column-title">{t(title)}</h1>
        {/* Saved from the header, as Twitter's "Edit profile" is. */}
        {section === 'profile' && (
          <button class="btn btn-sm btn-primary column-header-end" type="submit" form="profile-form">
            {t('Settings.Save')}
          </button>
        )}
      </header>
      <FormErrors errors={errors} />
      {section === 'profile' && <ProfileSection props={props} errors={errors} />}
      {section === 'privacy' && <PrivacySection props={props} />}
      {section === 'account' && <AccountSection props={props} />}
      {section === 'email' && <EmailSection props={props} errors={errors} />}
      {section === 'password' && <PasswordSection props={props} errors={errors} />}
      {section === 'passkeys' && <PasskeysSection props={props} />}
      {section === 'display' && <DisplaySection props={props} errors={errors} />}
      {section === 'sessions' && <SessionsSection props={props} />}
    </section>
  );
}

function ProfileSection({ props, errors }: SectionProps) {
  return (
    <>
      <ProfileImages avatar={props.avatar} banner={props.banner} />
      <form class="settings-form" id="profile-form" method="post" action={action('profile', 'Profile')} onSubmit={sending()}>
        <FloatField errors={errors} name="DisplayName" label={t('Field.DisplayName')} max={props.limits.displayNameMaxLength} initial={props.displayName}>
          <input class="float-field-input" {...field(errors, 'DisplayName')} defaultValue={props.displayName} maxLength={props.limits.displayNameMaxLength} required />
        </FloatField>
        <FloatField errors={errors} name="Bio" label={t('Field.Bio')} max={props.limits.bioMaxLength} initial={props.bio}>
          <textarea class="float-field-input" {...field(errors, 'Bio')} rows={3} maxLength={props.limits.bioMaxLength} defaultValue={props.bio ?? ''} />
        </FloatField>
        <Antiforgery />
      </form>
    </>
  );
}

/** The switches, by the field each one sends and the strings that say what it hides. */
const PRIVACY: Array<{ name: 'HideLikes' | 'HideFollows' | 'HideFollowCounts'; key: keyof Privacy; title: string; help: string }> = [
  { name: 'HideLikes', key: 'hideLikes', title: 'Settings.Privacy.HideLikes', help: 'Settings.Privacy.HideLikes.Help' },
  { name: 'HideFollows', key: 'hideFollows', title: 'Settings.Privacy.HideFollows', help: 'Settings.Privacy.HideFollows.Help' },
  { name: 'HideFollowCounts', key: 'hideFollowCounts', title: 'Settings.Privacy.HideFollowCounts', help: 'Settings.Privacy.HideFollowCounts.Help' },
];

/**
 * What the profile shows others, a switch each, all in one form sent whenever one is flipped. The switches show what
 * is saved: the answer's props, so one refused flips back.
 */
function PrivacySection({ props }: { props: SettingsProps }) {
  return (
    <>
      <p class="settings-intro">{t('Settings.Privacy.Intro')}</p>
      <form class="settings-switches" method="post" action={action('privacy', 'Privacy')} onSubmit={sending()}>
        {PRIVACY.map((option) => (
          <label key={option.name} class="settings-link switch">
            <span class="settings-link-text">
              <span class="settings-link-title">{t(option.title)}</span>
              <span class="settings-link-help">{t(option.help)}</span>
            </span>
            <input
              type="checkbox"
              name={option.name}
              value="true"
              checked={props.privacy[option.key]}
              onChange={(event) => event.currentTarget.form?.requestSubmit()}
            />
          </label>
        ))}
        <Antiforgery />
      </form>
    </>
  );
}

/**
 * Twitter's "Account information": the username, which never changes, then the ways to the email, the password and the
 * passkeys, then the providers the account signs in with.
 */
function AccountSection({ props }: { props: SettingsProps }) {
  return (
    <>
      <p class="settings-intro">{t('Settings.Account.Help')}</p>
      <div class="settings-facts">
        <div class="settings-link">
          <Icon name="alternate_email" />
          <span class="settings-link-text">
            <span class="settings-link-title">{t('Field.Username')}</span>
            <span class="settings-link-help">@{props.handle}</span>
          </span>
        </div>
      </div>
      <nav class="settings-nav" aria-label={t('Settings.Account')}>
        <LinkRow href={paths.settingsSection('email')} icon={<Icon name="mail" />} title={t('Field.Email')} help={props.email} />
        {props.hasPassword
          ? <LinkRow href={paths.settingsSection('password')} icon={<Icon name="key" />} title={t('Settings.ChangePassword')} help={t('Settings.ChangePassword.Help')} />
          : <LinkRow href={paths.settingsSection('password')} icon={<Icon name="key" />} title={t('Settings.SetPassword')} help={t('Settings.SetPassword.Help')} />}
        <LinkRow
          href={paths.settingsSection('passkeys')}
          icon={<Icon name="passkey" />}
          title={t('Settings.Passkeys')}
          help={t(props.passkeyTwoFactor ? 'Settings.Passkeys.TwoFactorOn' : 'Settings.Passkeys.Help')}
        />
      </nav>
      <section class="settings-connections" aria-labelledby="connections-title">
        <h2 class="settings-heading" id="connections-title">{t('Settings.Connections')}</h2>
        {props.connections.map((connection) => (
          <div key={connection.provider} class="settings-link">
            <span class={`brand-logo brand-logo-${connection.provider}`} aria-hidden="true" />
            <span class="settings-link-text">
              <span class="settings-link-title">{connection.label}</span>
              <span class="settings-link-help">{connection.name ?? t('Settings.NotConnected')}</span>
            </span>
            {connection.name
              ? (
                <form method="post" action={action('account', 'Unlink')} data-confirm={t('Settings.Disconnect.Confirm', connection.label)} onSubmit={sending()}>
                  <input type="hidden" name="Provider" value={connection.provider} />
                  <button class="btn btn-sm" type="submit">
                    {t('Settings.Disconnect')}
                  </button>
                  <Antiforgery />
                </form>
              )
              : (
                // Sent the plain way (the router leaves /oauth/ alone): the browser goes to the provider and back.
                <form method="post" action={`${paths.external(connection.provider)}?handler=Link`}>
                  <button class="btn btn-sm" type="submit">
                    {t('Settings.Connect')}
                  </button>
                  <Antiforgery />
                </form>
              )}
          </div>
        ))}
      </section>
    </>
  );
}

/** The current password, asked again before a change; an account without one has none to give. */
function CurrentPassword({ props, errors }: SectionProps) {
  if (!props.hasPassword) return null;
  return (
    <div class="settings-field-group">
      <FloatField errors={errors} name="CurrentPassword" label={t('Field.CurrentPassword')}>
        <input class="float-field-input" {...field(errors, 'CurrentPassword')} type="password" autocomplete="current-password" required />
      </FloatField>
      <a class="settings-aside-link" href={paths.forgotPassword}>
        {t('Login.Forgot')}
      </a>
    </div>
  );
}

/** Two steps: the new address and the password send a code there; the code then moves the account. */
function EmailSection({ props, errors }: SectionProps) {
  if (props.pendingEmail) {
    return (
      <>
        <p class="settings-intro">{t('Settings.Email.CodeSent', props.pendingEmail, props.codeLifetimeMinutes)}</p>
        <form class="settings-form" method="post" action={action('email', 'ConfirmEmail')} onSubmit={sending()}>
          <FloatField errors={errors} name="Code" label={t('Field.Code')}>
            <input
              class="float-field-input"
              {...field(errors, 'Code')}
              inputMode="numeric"
              autocomplete="one-time-code"
              pattern={`[0-9]{${props.codeLength}}`}
              maxLength={props.codeLength}
              required
            />
          </FloatField>
          <div class="form-actions">
            <button class="btn btn-primary" type="submit">
              {t('Verify.Submit')}
            </button>
          </div>
          <Antiforgery />
        </form>
        <CodeResend class="settings-aside" action={action('email', 'ResendEmail')} resendAt={props.resendAt} />
        {/* The wrong address: the first step again. */}
        <form class="settings-aside" method="post" action={action('email', 'CancelEmail')} onSubmit={sending()}>
          <span>{t('Verify.WrongEmail')}</span>
          <button class="btn btn-ghost btn-sm" type="submit">
            {t('Verify.UseAnother')}
          </button>
          <Antiforgery />
        </form>
      </>
    );
  }
  return (
    <>
      <p class="settings-intro">{props.hasPassword ? t('Settings.Email.Help', props.email) : t('Settings.Email.Help.NoPassword', props.email)}</p>
      <form class="settings-form" method="post" action={action('email', 'Email')} onSubmit={sending()}>
        <input type="hidden" name="username" value={props.handle} autocomplete="username" />
        <FloatField errors={errors} name="NewEmail" label={t('Field.NewEmail')}>
          <input class="float-field-input" {...field(errors, 'NewEmail')} type="email" autocomplete="email" required />
        </FloatField>
        <CurrentPassword props={props} errors={errors} />
        <div class="form-actions">
          <button class="btn btn-primary" type="submit">
            {t('Settings.Email.Send')}
          </button>
        </div>
        <Antiforgery />
      </form>
    </>
  );
}

function PasswordSection({ props, errors }: SectionProps) {
  return (
    <>
      <p class="settings-intro">{props.hasPassword ? t('Settings.Password.Help') : t('Settings.SetPassword.Intro')}</p>
      <form class="settings-form" method="post" action={action('password', 'Password')} onSubmit={sending((form) => form.reset())}>
        <input type="hidden" name="username" value={props.handle} autocomplete="username" />
        <CurrentPassword props={props} errors={errors} />
        <FloatField errors={errors} name="NewPassword" label={t('Field.NewPassword')} help={t('Field.Password.Help', props.passwordMinLength)}>
          <input
            class="float-field-input"
            {...field(errors, 'NewPassword')}
            type="password"
            autocomplete="new-password"
            minLength={props.passwordMinLength}
            maxLength={props.passwordMaxLength}
            required
          />
        </FloatField>
        <div class="form-actions">
          <button class="btn btn-primary" type="submit">
            {t('Settings.Save')}
          </button>
        </div>
        <Antiforgery />
      </form>
    </>
  );
}

/** A passkey in a row like the devices: its icon, where it was added, when, and when it last signed in; then "Remove". */
function PasskeyRow({ passkey }: { passkey: Passkey }) {
  const name = passkey.name ?? t('Passkeys.Unnamed');
  return (
    <div class="settings-link">
      <Icon name="passkey" />
      <span class="settings-link-text">
        <span class="settings-link-title">{name}</span>
        {passkey.browser && <span class="settings-link-help">{passkey.browser}</span>}
        <span class="settings-link-help">
          {t('Passkeys.AddedAt')} <Time value={passkey.createdAt} />
          {' · '}
          {passkey.lastUsedAt ? <>{t('Passkeys.LastUsed')} <Time value={passkey.lastUsedAt} /></> : t('Passkeys.NeverUsed')}
        </span>
      </span>
      <form method="post" action={action('passkeys', 'RemovePasskey')} data-confirm={t('Passkeys.Remove.Confirm')} onSubmit={sending()}>
        <input type="hidden" name="PasskeyId" value={passkey.id} />
        <button class="btn btn-sm" type="submit">
          {t('Passkeys.Remove')}
        </button>
        <Antiforgery />
      </form>
    </div>
  );
}

/**
 * The account's passkeys, oldest first, each of which can be removed, and a button that adds another: the server's
 * options, the browser's dialog, then what the authenticator made, posted as the page's other forms are. Below them, a
 * switch between signing in with a passkey alone (off, as a new account starts) and asking for one after the password.
 */
function PasskeysSection({ props }: { props: SettingsProps }) {
  const passkeys = props.passkeys ?? [];
  const form = useRef<HTMLFormElement>(null);
  const credential = useRef<HTMLInputElement>(null);
  const ticket = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);

  const add = async (): Promise<void> => {
    if (!passkeysSupported()) {
      showToast(t('Passkeys.Unsupported'), true);
      return;
    }
    setBusy(true);
    try {
      const answer = await createPasskey(action('passkeys', 'PasskeyOptions'), formData());
      if (answer && form.current && credential.current && ticket.current) {
        credential.current.value = answer.credential;
        ticket.current.value = answer.ticket;
        await sendForm(form.current, null);
      }
    } catch (error) {
      showToast(error instanceof PasskeyError ? error.message : t('Passkeys.Failed'), true);
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <p class="settings-intro">{t('Settings.Passkeys.Intro')}</p>
      <section class="settings-devices" aria-labelledby="passkeys-title">
        <h2 class="settings-heading" id="passkeys-title">{t('Passkeys.Yours')}</h2>
        {passkeys.length === 0
          ? <p class="settings-devices-empty">{t('Passkeys.None')}</p>
          : passkeys.map((passkey) => <PasskeyRow key={passkey.id} passkey={passkey} />)}
        <form class="settings-aside" ref={form} method="post" action={action('passkeys', 'AddPasskey')}>
          <input type="hidden" name="Credential" ref={credential} />
          <input type="hidden" name="Ticket" ref={ticket} />
          <button class="btn btn-sm btn-primary" type="button" disabled={busy} onClick={() => void add()}>
            {t('Passkeys.Add')}
          </button>
          <Antiforgery />
        </form>
      </section>
      {passkeys.length > 0 && (
        <form class="settings-switches" method="post" action={action('passkeys', 'PasskeyTwoFactor')} onSubmit={sending()}>
          <label class="settings-link switch">
            <span class="settings-link-text">
              <span class="settings-link-title">{t('Passkeys.TwoFactor')}</span>
              <span class="settings-link-help">{t('Passkeys.TwoFactor.Help')}</span>
            </span>
            <input
              type="checkbox"
              name="TwoFactor"
              value="true"
              checked={props.passkeyTwoFactor ?? false}
              onChange={(event) => event.currentTarget.form?.requestSubmit()}
            />
          </label>
          <Antiforgery />
        </form>
      )}
    </>
  );
}

/** Twitter's "Display": the schemes as cards drawn in their own colours, applied as soon as one is picked. */
function DisplaySection({ props, errors }: SectionProps) {
  return (
    <>
      <p class="settings-intro">{t('Settings.Display.Help')}</p>
      <form
        class="settings-form"
        method="post"
        action={action('display', 'Theme')}
        onSubmit={sending((form) => {
          theme.value = String(new FormData(form).get('Theme') ?? 'system');
        })}
      >
        <fieldset class="settings-group">
          <legend class="settings-heading">{t('Settings.Theme')}</legend>
          <div class="theme-options">
            {props.themes.map((option) => (
              <label key={option.value} class={`theme-option theme-option-${option.value}`}>
                <input type="radio" name="Theme" value={option.value} checked={option.value === theme.value} onChange={(event) => event.currentTarget.form?.requestSubmit()} />
                <span>{option.label}</span>
              </label>
            ))}
          </div>
          <p class="help">{t('Settings.Theme.Help')}</p>
        </fieldset>
        <Antiforgery />
      </form>

      {/* Sent as a page, which then comes back in the new language. */}
      <form class="settings-form" method="post" action={action('display', 'Language')}>
        <fieldset class="settings-group">
          <legend class="settings-heading">{t('Settings.Language')}</legend>
          <FloatField errors={errors} name="Culture" label={t('Settings.Language.Field')}>
            <span class="float-field-select">
              <select class="float-field-input" id="Culture" name="Culture">
                {props.cultures.map((option) => (
                  <option key={option.value} value={option.value} lang={option.lang} selected={option.value === props.culture}>
                    {option.label}
                  </option>
                ))}
              </select>
              <Icon name="expand_more" />
            </span>
          </FloatField>
        </fieldset>
        <div class="form-actions">
          <button class="btn btn-primary" type="submit">
            {t('Settings.Save')}
          </button>
        </div>
        <Antiforgery />
      </form>
    </>
  );
}

/** The icon for a sort of device. */
function DeviceIcon({ kind }: { kind: Device['kind'] }) {
  switch (kind) {
    case 'phone':
      return <Icon name="smartphone" />;
    case 'tablet':
      return <Icon name="tablet" />;
    case 'computer':
      return <Icon name="computer" />;
    default:
      return <Icon name="devices" />;
  }
}

/** "Chrome · Shanghai, China": what the device was logged in with, and from where; empty when neither is known. */
function deviceDetails(device: Device): string {
  const place =
    device.locality && device.country ? t('Sessions.Location', device.locality, device.country) : (device.locality ?? device.country);
  return [device.browser, place].filter((part) => part).join(' · ');
}

/** A device in a row like the links: its icon, its name, how and where, and when; the button where their chevron is. */
function DeviceRow({ device }: { device: Device }) {
  const name = device.name ?? t('Sessions.UnknownDevice');
  const details = deviceDetails(device);
  return (
    <div class="settings-link">
      <DeviceIcon kind={device.kind} />
      <span class="settings-link-text">
        <span class="settings-link-title">{name}</span>
        {details && <span class="settings-link-help">{details}</span>}
        <span class="settings-link-help">
          {device.current ? t('Sessions.ActiveNow') : <>{t('Sessions.LastActive')} <Time value={device.lastSeenAt} /></>}
          {' · '}
          {t('Sessions.SignedIn')} <Time value={device.signedInAt} />
        </span>
      </span>
      {!device.current && (
        <form method="post" action={action('sessions', 'EndSession')} data-confirm={t('Sessions.Remove.Confirm', name)} onSubmit={sending()}>
          <input type="hidden" name="SessionId" value={device.id} />
          <button class="btn btn-sm" type="submit">
            {t('Sessions.Remove')}
          </button>
          <Antiforgery />
        </form>
      )}
    </div>
  );
}

/**
 * Twitter's "Sessions": this device on its own, then the others, most recently used first, each of which can be
 * logged out, or all of them at once. The list follows log-ins and log-outs elsewhere.
 */
function SessionsSection({ props }: { props: SettingsProps }) {
  const devices = props.devices ?? [];
  const current = devices.filter((device) => device.current);
  const others = devices.filter((device) => !device.current);
  return (
    <>
      <p class="settings-intro">{t('Settings.Sessions.Help')}</p>
      {current.length > 0 && (
        <section class="settings-devices" aria-labelledby="current-device-title">
          <h2 class="settings-heading" id="current-device-title">{t('Sessions.Current')}</h2>
          {current.map((device) => <DeviceRow key={device.id} device={device} />)}
        </section>
      )}
      <section class="settings-devices" aria-labelledby="other-devices-title">
        <h2 class="settings-heading" id="other-devices-title">{t('Sessions.Others')}</h2>
        {others.length === 0
          ? <p class="settings-devices-empty">{t('Sessions.None')}</p>
          : (
            <>
              {others.map((device) => <DeviceRow key={device.id} device={device} />)}
              <form class="settings-aside" method="post" action={action('sessions', 'EndOtherSessions')} data-confirm={t('Sessions.EndOthers.Confirm')} onSubmit={sending()}>
                <button class="btn btn-sm btn-soft-danger" type="submit">
                  {t('Sessions.EndOthers')}
                </button>
                <Antiforgery />
              </form>
            </>
          )}
      </section>
    </>
  );
}
