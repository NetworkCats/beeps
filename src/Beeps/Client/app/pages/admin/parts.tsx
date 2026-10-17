// What several admin pages draw the same way: a person's badges, a beep's moderation status, an admin's buttons for
// an agent, and the admin addresses.
import { useEffect, useRef, useState } from 'preact/hooks';
import { copyText } from '../../clipboard.ts';
import { Antiforgery } from '../../components/Forms.tsx';
import { sending } from '../../forms.ts';
import { t } from '../../i18n.ts';

/** The admin pages' addresses, as Url.Page builds them for the Razor templates. */
export const adminPaths = {
  users: '/admin/users',
  user: (id: string): string => `/admin/users/${id}`,
  agents: '/admin/agents',
  review: '/admin/review',
  beep: (id: string): string => `/admin/beeps/${id}`,
  system: '/admin/system',
  settings: '/admin/settings',
  /** A handler of a page whose route has no id, for one row: the id goes in the query, before the handler. */
  rowAction: (page: string, id: string, handler: string): string => `${page}?id=${id}&handler=${handler}`,
};

/** AdminUserProps. */
export interface AdminUser {
  id: string;
  handle: string;
  displayName: string;
  email: string;
  admin?: boolean;
  emailVerified?: boolean;
  createdAt: string;
  bannedAt?: string;
  deletedAt?: string;
}

export function UserBadges({ user }: { user: AdminUser }) {
  return (
    <span class="tag-group">
      {user.deletedAt !== undefined && <span class="badge badge-danger">{t('Admin.Badge.Deleted')}</span>}
      {user.bannedAt !== undefined && <span class="badge badge-danger">{t('Admin.Badge.Banned')}</span>}
      {user.admin && <span class="badge badge-primary">{t('Admin.Badge.Admin')}</span>}
      {!user.emailVerified && <span class="badge badge-warning">{t('Admin.Badge.Unverified')}</span>}
    </span>
  );
}

// ModerationStatus, by name: its tone and its label.
const STATUSES: Record<string, { tone: string; label: string }> = {
  Pending: { tone: '', label: 'Moderation.Status.Pending' },
  Allowed: { tone: '', label: 'Moderation.Status.Allowed' },
  Hidden: { tone: ' badge-danger', label: 'Moderation.Status.Hidden' },
  Failed: { tone: ' badge-warning', label: 'Moderation.Status.Failed' },
  HumanAllowed: { tone: ' badge-success', label: 'Moderation.Status.HumanAllowed' },
};

export function ModerationStatus({ status }: { status: string }) {
  const shown = STATUSES[status];
  return <span class={`badge${shown?.tone ?? ''}`}>{shown ? t(shown.label) : status}</span>;
}

/** The admin's buttons for one agent; they post to the agents page, which comes back to `returnUrl`. */
export function AgentActions({ id, disabled, hasKey, returnUrl }: { id: string; disabled?: boolean; hasKey?: boolean; returnUrl: string }) {
  const action = (handler: string): string => `${adminPaths.agents}?id=${id}&returnUrl=${encodeURIComponent(returnUrl)}&handler=${handler}`;
  return (
    <div class="admin-actions admin-actions-inline">
      {disabled ? (
        <form method="post" action={action('Enable')} onSubmit={sending()}>
          <button class="btn btn-sm" type="submit">
            {t('Agents.Enable')}
          </button>
          <Antiforgery />
        </form>
      ) : (
        <form method="post" action={action('Disable')} data-confirm={t('Admin.Agents.Disable.Confirm')} onSubmit={sending()}>
          <button class="btn btn-sm" type="submit">
            {t('Agents.Disable')}
          </button>
          <Antiforgery />
        </form>
      )}
      {hasKey && (
        <form method="post" action={action('RevokeKey')} data-confirm={t('Admin.Agents.RevokeKey.Confirm')} onSubmit={sending()}>
          <button class="btn btn-sm" type="submit">
            {t('Admin.Agents.RevokeKey')}
          </button>
          <Antiforgery />
        </form>
      )}
      <form method="post" action={action('Delete')} data-confirm={t('Admin.Agents.Delete.Confirm')} onSubmit={sending()}>
        <button class="btn btn-sm btn-danger" type="submit">
          {t('Admin.Delete')}
        </button>
        <Antiforgery />
      </form>
    </div>
  );
}

const COPIED_MS = 1500;

/** A code with a button that copies it (<div class="copy-box">). */
export function CopyBox({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);
  const timer = useRef<number | undefined>(undefined);
  useEffect(() => () => window.clearTimeout(timer.current), []);
  const copy = async (): Promise<void> => {
    if (!(await copyText(text))) return;
    setCopied(true);
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => setCopied(false), COPIED_MS);
  };
  return (
    <div class="copy-box">
      <code>{text}</code>
      <button class="btn-copy" type="button" data-copied={copied ? '' : undefined} onClick={() => void copy()}>
        {copied ? t('Copy.Done') : t('Copy.Action')}
      </button>
    </div>
  );
}
