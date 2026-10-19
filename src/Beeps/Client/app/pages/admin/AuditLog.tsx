// What admins did, newest first (Admin/AuditLog.cshtml). Read-only. Fetched again after any change on the site (every
// admin action writes an entry); older entries load as the table is scrolled.
import { Pager } from '../../components/Pager.tsx';
import { Time } from '../../components/Time.tsx';
import { t } from '../../i18n.ts';
import type { IslandProps } from '../../islands.tsx';
import { useLive } from '../../page.ts';
import type { LiveProps, PageProps } from '../../props.ts';
import { useRows } from '../../rows.ts';
import { adminPaths } from './parts.tsx';

interface AuditEntry {
  id: string;
  createdAt: string;
  adminId: string;
  adminHandle: string;
  /** Localized. */
  action: string;
  /** What it was done to, localized. */
  target: string;
  /** The admin page about that, when there is one. */
  targetUrl?: string;
  detail?: string;
}

interface AdminAuditProps {
  live: LiveProps;
  entries: PageProps<AuditEntry>;
}

export function AuditLogPage({ props }: IslandProps<AdminAuditProps>) {
  useLive(props.live);
  const { rows, next, loadMore } = useRows(props, {
    island: 'admin-audit',
    key: (entry: AuditEntry) => entry.id,
    pick: (p) => p.entries,
    snapshot: (entries) => ({ ...props, entries }),
  });
  return (
    <>
      <h1>{t('Admin.AuditLog')}</h1>
      {rows.length === 0 ? (
        <div class="empty empty-sm">
          <p class="empty-text">{t('Admin.AuditLog.None')}</p>
        </div>
      ) : (
        <>
          <div class="table-container">
            <table class="table table-stack">
              <thead>
                <tr>
                  <th>{t('Admin.AuditLog.When')}</th>
                  <th>{t('Admin.AuditLog.Who')}</th>
                  <th>{t('Admin.AuditLog.What')}</th>
                  <th>{t('Admin.AuditLog.Target')}</th>
                  <th>{t('Admin.AuditLog.Detail')}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((entry) => (
                  <tr key={entry.id}>
                    <td data-label={t('Admin.AuditLog.When')}>
                      <Time value={entry.createdAt} />
                    </td>
                    <td data-label={t('Admin.AuditLog.Who')}>
                      <a href={adminPaths.user(entry.adminId)}>@{entry.adminHandle}</a>
                    </td>
                    <td data-label={t('Admin.AuditLog.What')}>{entry.action}</td>
                    <td data-label={t('Admin.AuditLog.Target')}>{entry.targetUrl === undefined ? entry.target : <a href={entry.targetUrl}>{entry.target}</a>}</td>
                    <td data-label={t('Admin.AuditLog.Detail')}>{entry.detail}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {next && <Pager key={next} href={next} label={t('Admin.Older')} small ariaLabel={t('Admin.Pager')} onMore={loadMore} />}
        </>
      )}
    </>
  );
}
