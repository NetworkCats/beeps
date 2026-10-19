// Registration mode and invite codes (Admin/Settings.cshtml). Fetched again after any change on the site (another
// admin's settings, an invite used to sign up). A new invite's full code comes only in the answer that created it: it
// stays shown while that invite is still open, and goes once it is used or revoked.
import { useRef } from 'preact/hooks';
import { Antiforgery } from '../../components/Forms.tsx';
import { Time } from '../../components/Time.tsx';
import { sending } from '../../forms.ts';
import { t } from '../../i18n.ts';
import type { IslandProps } from '../../islands.tsx';
import { useLive } from '../../page.ts';
import type { LiveProps } from '../../props.ts';
import { adminPaths, CopyBox } from './parts.tsx';

interface RegistrationMode {
  value: string;
  label: string;
  help: string;
}

interface Invite {
  id: string;
  codePrefix: string;
  createdAt: string;
  expiresAt: string;
  used?: boolean;
  usedByHandle?: string;
}

interface NewInvite {
  id: string;
  code: string;
}

interface AdminSettingsProps {
  live: LiveProps;
  mode: string;
  modes: RegistrationMode[];
  invites: Invite[];
  newInvite?: NewInvite;
}

export function SettingsPage({ props }: IslandProps<AdminSettingsProps>) {
  useLive(props.live);
  const shown = useRef(props.newInvite);
  if (props.newInvite) shown.current = props.newInvite;
  const open = shown.current?.id;
  if (open !== undefined && !props.invites.some((invite) => invite.id === open && !invite.used)) shown.current = undefined;
  const created = shown.current;
  return (
    <>
      <h1>{t('Admin.Settings')}</h1>

      <section class="admin-section">
        <h2>{t('Admin.Registration')}</h2>
        <form method="post" action={`${adminPaths.settings}?handler=Mode`} onSubmit={sending()}>
          {/* Drawn anew when the saved mode changes (here, or by another admin); a pick not saved yet stays until then. */}
          <fieldset class="check-group" key={props.mode}>
            <legend class="visually-hidden">{t('Admin.Registration')}</legend>
            {props.modes.map((mode) => (
              <label key={mode.value} class="radio">
                <input type="radio" name="Mode" value={mode.value} defaultChecked={props.mode === mode.value} />
                <span>
                  <strong>{mode.label}</strong> <span class="help">{mode.help}</span>
                </span>
              </label>
            ))}
          </fieldset>
          <div class="form-actions">
            <button class="btn btn-primary btn-sm" type="submit">
              {t('Admin.Save')}
            </button>
          </div>
          <Antiforgery />
        </form>
      </section>

      <section class="admin-section">
        <header class="section-header">
          <h2>{t('Admin.Invites')}</h2>
          <form method="post" action={`${adminPaths.settings}?handler=CreateInvite`} onSubmit={sending()}>
            <button class="btn btn-sm" type="submit">
              {t('Admin.Invites.Create')}
            </button>
            <Antiforgery />
          </form>
        </header>
        {created && (
          <div class="alert alert-success" role="status">
            <div class="alert-content">
              <p class="alert-text">{t('Admin.Invites.Created')}</p>
              <CopyBox text={created.code} />
            </div>
          </div>
        )}
        {props.invites.length === 0 ? (
          <p class="section-lead">{t('Admin.Invites.None')}</p>
        ) : (
          <div class="table-container">
            <table class="table table-stack">
              <thead>
                <tr>
                  <th>{t('Admin.Invites.Code')}</th>
                  <th>{t('Admin.Invites.Created.At')}</th>
                  <th>{t('Admin.Invites.Expires')}</th>
                  <th>{t('Admin.Invites.UsedBy')}</th>
                  <th>
                    <span class="visually-hidden">{t('Admin.Actions')}</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {props.invites.map((invite) => (
                  <tr key={invite.id}>
                    <td data-label={t('Admin.Invites.Code')}>
                      <code>{invite.codePrefix}…</code>
                    </td>
                    <td data-label={t('Admin.Invites.Created.At')}>
                      <Time value={invite.createdAt} />
                    </td>
                    <td data-label={t('Admin.Invites.Expires')}>
                      <Time value={invite.expiresAt} />
                    </td>
                    <td data-label={t('Admin.Invites.UsedBy')}>{invite.usedByHandle === undefined ? '—' : `@${invite.usedByHandle}`}</td>
                    <td class="table-actions">
                      {!invite.used && (
                        <form
                          method="post"
                          action={adminPaths.rowAction(adminPaths.settings, invite.id, 'RevokeInvite')}
                          data-confirm={t('Admin.Invites.Revoke.Confirm')}
                          onSubmit={sending()}
                        >
                          <button class="btn btn-ghost btn-xs" type="submit">
                            {t('Admin.Invites.Revoke')}
                          </button>
                          <Antiforgery />
                        </form>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  );
}
