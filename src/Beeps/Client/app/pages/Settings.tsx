// The account's own settings (Settings.cshtml): avatar, profile, language, colour scheme and password. Each form is sent
// in the background and answered in place; the saved name, bio and avatar follow changes made in another tab, unless
// they are being edited here. A new language shows the page anew in it.
import { AvatarEditor, type AvatarEditorProps } from '../components/AvatarEditor.tsx';
import { Antiforgery, FieldError, FormErrors, field } from '../components/Forms.tsx';
import { sending } from '../forms.ts';
import { t } from '../i18n.ts';
import type { IslandProps } from '../islands.tsx';
import { useLive } from '../page.ts';
import { paths } from '../paths.ts';
import type { LiveProps } from '../props.ts';
import { theme } from '../state.ts';

interface Option {
  value: string;
  label: string;
  lang?: string;
}

export interface ProfileLimits {
  displayNameMaxLength: number;
  bioMaxLength: number;
}

interface SettingsProps {
  live: LiveProps;
  avatar: AvatarEditorProps;
  handle: string;
  displayName?: string;
  bio?: string;
  culture: string;
  cultures: Option[];
  theme: string;
  themes: Option[];
  limits: ProfileLimits;
  passwordMinLength: number;
  passwordMaxLength: number;
}

export function SettingsPage({ props, errors }: IslandProps<SettingsProps>) {
  useLive(props.live);
  const action = (handler: string): string => `${paths.settings}?handler=${handler}`;
  return (
    <section class="feed settings">
      <header class="column-header">
        <h1 class="column-title">{t('Settings.Title')}</h1>
      </header>
      <FormErrors errors={errors} />

      <AvatarEditor editor={props.avatar} />

      <form class="settings-section" method="post" action={action('Profile')} aria-labelledby="profile-title" onSubmit={sending()}>
        <h2 id="profile-title">{t('Settings.Profile')}</h2>
        <div class="field">
          <span class="label">{t('Field.Username')}</span>
          <p class="settings-fixed">@{props.handle}</p>
        </div>
        <div class="field">
          <label class="label" for="DisplayName">
            {t('Field.DisplayName')}
          </label>
          <input class="input" {...field(errors, 'DisplayName')} defaultValue={props.displayName} maxLength={props.limits.displayNameMaxLength} required />
          <FieldError errors={errors} name="DisplayName" />
        </div>
        <div class="field">
          <label class="label" for="Bio">
            {t('Field.Bio')}
          </label>
          <textarea class="textarea" {...field(errors, 'Bio')} rows={3} maxLength={props.limits.bioMaxLength} defaultValue={props.bio ?? ''} />
          <p class="help">{t('Field.Bio.Help', props.limits.bioMaxLength)}</p>
          <FieldError errors={errors} name="Bio" />
        </div>
        <div class="form-actions">
          <button class="btn btn-primary" type="submit">
            {t('Settings.Save')}
          </button>
        </div>
        <Antiforgery />
      </form>

      {/* Sent as a page, which then comes back in the new language. */}
      <form class="settings-section" method="post" action={action('Language')} aria-labelledby="language-title">
        <h2 id="language-title">{t('Settings.Language')}</h2>
        <div class="field">
          <label class="visually-hidden" for="Culture">
            {t('Settings.Language')}
          </label>
          <select class="select settings-select" id="Culture" name="Culture">
            {props.cultures.map((option) => (
              <option key={option.value} value={option.value} lang={option.lang} selected={option.value === props.culture}>
                {option.label}
              </option>
            ))}
          </select>
        </div>
        <div class="form-actions">
          <button class="btn" type="submit">
            {t('Settings.Save')}
          </button>
        </div>
        <Antiforgery />
      </form>

      <form
        class="settings-section"
        method="post"
        action={action('Theme')}
        aria-labelledby="theme-title"
        onSubmit={sending((form) => {
          theme.value = String(new FormData(form).get('Theme') ?? 'system');
        })}
      >
        <h2 id="theme-title">{t('Settings.Theme')}</h2>
        <div class="field">
          <label class="visually-hidden" for="Theme">
            {t('Settings.Theme')}
          </label>
          <select class="select settings-select" id="Theme" name="Theme">
            {props.themes.map((option) => (
              <option key={option.value} value={option.value} selected={option.value === theme.value}>
                {option.label}
              </option>
            ))}
          </select>
          <p class="help">{t('Settings.Theme.Help')}</p>
        </div>
        <div class="form-actions">
          <button class="btn" type="submit">
            {t('Settings.Save')}
          </button>
        </div>
        <Antiforgery />
      </form>

      <form class="settings-section" method="post" action={action('Password')} aria-labelledby="password-title" onSubmit={sending((form) => form.reset())}>
        <h2 id="password-title">{t('Settings.Password')}</h2>
        <p class="help">{t('Settings.Password.Help')}</p>
        <input type="hidden" name="username" value={props.handle} autocomplete="username" />
        <div class="field">
          <label class="label" for="CurrentPassword">
            {t('Field.CurrentPassword')}
          </label>
          <input class="input" {...field(errors, 'CurrentPassword')} type="password" autocomplete="current-password" required />
          <FieldError errors={errors} name="CurrentPassword" />
        </div>
        <div class="field">
          <label class="label" for="NewPassword">
            {t('Field.NewPassword')}
          </label>
          <input
            class="input"
            {...field(errors, 'NewPassword')}
            type="password"
            autocomplete="new-password"
            minLength={props.passwordMinLength}
            maxLength={props.passwordMaxLength}
            required
          />
          <p class="help">{t('Field.Password.Help', props.passwordMinLength)}</p>
          <FieldError errors={errors} name="NewPassword" />
        </div>
        <div class="form-actions">
          <button class="btn" type="submit">
            {t('Settings.ChangePassword')}
          </button>
        </div>
        <Antiforgery />
      </form>
    </section>
  );
}
