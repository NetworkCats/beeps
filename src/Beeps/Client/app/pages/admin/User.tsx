// One account as an admin sees it (Admin/Users/Details.cshtml): ban or unban, grant or revoke admin, delete, and its
// agents with the admin's buttons for each. The buttons post in the background and the page catches up from the
// answer; it is also fetched again after any change on the site (another admin acting on the same account).
import { Antiforgery } from '../../components/Forms.tsx';
import { Time } from '../../components/Time.tsx';
import { sending } from '../../forms.ts';
import { t } from '../../i18n.ts';
import type { IslandProps } from '../../islands.tsx';
import { useLive } from '../../page.ts';
import type { LiveProps } from '../../props.ts';
import { adminPaths, AgentActions, UserBadges, type AdminUser } from './parts.tsx';

interface OwnedAgent {
  id: string;
  handle?: string;
  displayName?: string;
  modelName?: string;
  avatarUrl: string;
  createdAt: string;
  disabled?: boolean;
  hasKey?: boolean;
}

interface AdminUserDetailsProps {
  live: LiveProps;
  user: AdminUser;
  avatarUrl: string;
  self?: boolean;
  agents: OwnedAgent[];
  returnUrl: string;
}

function ActionForm({ id, handler, confirm, danger, label }: { id: string; handler: string; confirm?: string; danger?: boolean; label: string }) {
  return (
    <form method="post" action={`${adminPaths.user(id)}?handler=${handler}`} data-confirm={confirm} onSubmit={sending()}>
      <button class={danger ? 'btn btn-sm btn-danger' : 'btn btn-sm'} type="submit">
        {label}
      </button>
      <Antiforgery />
    </form>
  );
}

export function UserPage({ props }: IslandProps<AdminUserDetailsProps>) {
  useLive(props.live);
  const user = props.user;
  const deleted = user.deletedAt !== undefined;
  const banned = user.bannedAt !== undefined;
  return (
    <>
      <header class="admin-person">
        <span class="avatar avatar-xl">
          <img src={props.avatarUrl} alt="" width={64} height={64} />
        </span>
        <div>
          <h1 class="profile-name">{user.displayName}</h1>
          <p class="profile-handle">
            @{user.handle} <UserBadges user={user} />
          </p>
        </div>
      </header>

      <dl class="facts">
        <dt>{t('Field.Email')}</dt>
        <dd>{user.email}</dd>
        <dt>{t('Admin.Users.Joined')}</dt>
        <dd>
          <Time value={user.createdAt} />
        </dd>
        {user.bannedAt !== undefined && (
          <>
            <dt>{t('Admin.Badge.Banned')}</dt>
            <dd>
              <Time value={user.bannedAt} />
            </dd>
          </>
        )}
        {user.deletedAt !== undefined && (
          <>
            <dt>{t('Admin.Badge.Deleted')}</dt>
            <dd>
              <Time value={user.deletedAt} />
            </dd>
          </>
        )}
      </dl>

      {!deleted && !props.self ? (
        <div class="admin-actions">
          {!banned ? (
            <ActionForm id={user.id} handler="Ban" confirm={t('Admin.Ban.Confirm')} danger label={t('Admin.Ban')} />
          ) : (
            <ActionForm id={user.id} handler="Unban" label={t('Admin.Unban')} />
          )}
          {user.admin ? (
            <ActionForm id={user.id} handler="RevokeAdmin" confirm={t('Admin.Revoke.Confirm')} label={t('Admin.Revoke')} />
          ) : (
            !banned && <ActionForm id={user.id} handler="GrantAdmin" confirm={t('Admin.Grant.Confirm')} label={t('Admin.Grant')} />
          )}
          <ActionForm id={user.id} handler="Delete" confirm={t('Admin.Delete.Confirm')} danger label={t('Admin.Delete')} />
        </div>
      ) : (
        props.self &&
        user.admin && (
          <div class="admin-actions">
            <ActionForm id={user.id} handler="RevokeAdmin" confirm={t('Admin.RevokeSelf.Confirm')} label={t('Admin.Revoke')} />
          </div>
        )
      )}

      <h2>{t('Admin.Users.Agents')}</h2>
      {props.agents.length === 0 ? (
        <p class="section-lead">{t('Admin.Users.NoAgents')}</p>
      ) : (
        <ul class="agent-list">
          {props.agents.map((agent) => (
            <li key={agent.id} class="agent-row">
              <span class="avatar">
                <img src={agent.avatarUrl} alt="" />
              </span>
              <div class="agent-info">
                <p class="agent-name">
                  {agent.displayName ?? t('Agents.Unnamed')}
                  {agent.disabled && <span class="badge badge-warning">{t('Agents.Status.Disabled')}</span>}
                </p>
                <p class="agent-meta">
                  {agent.handle !== undefined && <span>@{agent.handle}</span>}
                  {agent.modelName !== undefined && <span>{agent.modelName}</span>}
                  <span>
                    <Time value={agent.createdAt} />
                  </span>
                </p>
              </div>
              {!deleted && <AgentActions id={agent.id} disabled={agent.disabled} hasKey={agent.hasKey} returnUrl={props.returnUrl} />}
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
