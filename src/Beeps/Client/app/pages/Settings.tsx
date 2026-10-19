// The account's own settings (Settings.cshtml), split as Twitter splits them: /settings lists the sections, and each
// has a page of its own, with a way back to the list. Profile is Twitter's "Edit profile" (the banner and avatar, the
// name and bio, saved from the header); privacy holds a switch for each thing the profile can keep to the account, sent
// as soon as it is flipped; account shows the username and leads to the email's and the password's pages,
// the email moving only once the code sent to the new address is entered; display holds the colour scheme, applied as
// soon as one is picked, and the language. Each form is sent in the background and
// answered in place; the saved name, bio, avatar and banner follow changes made in another tab, unless they are being
// edited here. A new language shows the page anew in it.
import type { ComponentChildren } from 'preact';
import { FloatField } from '../components/FloatField.tsx';
import { Antiforgery, FormErrors, field } from '../components/Forms.tsx';
import { Icon } from '../components/Icon.tsx';
import { CodeResend } from '../components/CodeResend.tsx';
import { ProfileImages, type AvatarEditorProps, type BannerEditorProps } from '../components/ProfileImageEditor.tsx';
import { sending } from '../forms.ts';
import { t } from '../i18n.ts';
import type { IslandProps } from '../islands.tsx';
import { useLive } from '../page.ts';
import { paths } from '../paths.ts';
import type { Errors, LiveProps } from '../props.ts';
import { theme } from '../state.ts';

type Section = 'profile' | 'privacy' | 'account' | 'email' | 'password' | 'display';

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
  display: { title: 'Settings.Display', back: paths.settings },
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
        </nav>
      </section>
    );
  }
  const { title, back } = TITLES[section];
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
      {section === 'display' && <DisplaySection props={props} errors={errors} />}
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

/** Twitter's "Account information": the username, which never changes, then the ways to the email and the password. */
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
        <LinkRow href={paths.settingsSection('password')} icon={<Icon name="key" />} title={t('Settings.ChangePassword')} help={t('Settings.ChangePassword.Help')} />
      </nav>
    </>
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
      <p class="settings-intro">{t('Settings.Email.Help', props.email)}</p>
      <form class="settings-form" method="post" action={action('email', 'Email')} onSubmit={sending()}>
        <input type="hidden" name="username" value={props.handle} autocomplete="username" />
        <FloatField errors={errors} name="NewEmail" label={t('Field.NewEmail')}>
          <input class="float-field-input" {...field(errors, 'NewEmail')} type="email" autocomplete="email" required />
        </FloatField>
        <div class="settings-field-group">
          <FloatField errors={errors} name="CurrentPassword" label={t('Field.CurrentPassword')}>
            <input class="float-field-input" {...field(errors, 'CurrentPassword')} type="password" autocomplete="current-password" required />
          </FloatField>
          <a class="settings-aside-link" href={paths.forgotPassword}>
            {t('Login.Forgot')}
          </a>
        </div>
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
      <p class="settings-intro">{t('Settings.Password.Help')}</p>
      <form class="settings-form" method="post" action={action('password', 'Password')} onSubmit={sending((form) => form.reset())}>
        <input type="hidden" name="username" value={props.handle} autocomplete="username" />
        <div class="settings-field-group">
          <FloatField errors={errors} name="CurrentPassword" label={t('Field.CurrentPassword')}>
            <input class="float-field-input" {...field(errors, 'CurrentPassword')} type="password" autocomplete="current-password" required />
          </FloatField>
          <a class="settings-aside-link" href={paths.forgotPassword}>
            {t('Login.Forgot')}
          </a>
        </div>
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
