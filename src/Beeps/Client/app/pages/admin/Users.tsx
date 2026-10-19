// Every account, newest first, or those a search finds (Admin/Users/Index.cshtml). Fetched again after any change on
// the site (a sign-up, a ban by another admin); older pages load as the table is scrolled.
import { Pager } from '../../components/Pager.tsx';
import { Time } from '../../components/Time.tsx';
import { t } from '../../i18n.ts';
import type { IslandProps } from '../../islands.tsx';
import { useLive } from '../../page.ts';
import type { LiveProps, PageProps } from '../../props.ts';
import { useRows } from '../../rows.ts';
import { adminPaths, UserBadges, type AdminUser } from './parts.tsx';

interface AdminUsersProps {
  live: LiveProps;
  q?: string;
  users: PageProps<AdminUser>;
}

export function UsersPage({ props }: IslandProps<AdminUsersProps>) {
  useLive(props.live);
  const { rows, next, loadMore } = useRows(props, {
    island: 'admin-users',
    key: (user: AdminUser) => user.id,
    pick: (p) => p.users,
    snapshot: (users) => ({ ...props, users }),
  });
  return (
    <>
      <header class="section-header">
        <h1>{t('Admin.Users')}</h1>
        <form class="admin-search" method="get" role="search">
          <input class="input input-sm" type="search" name="q" defaultValue={props.q} placeholder={t('Admin.Users.Search')} aria-label={t('Admin.Users.Search')} maxLength={100} />
          <button class="btn btn-sm" type="submit">
            {t('Admin.Search')}
          </button>
        </form>
      </header>
      {rows.length === 0 ? (
        <div class="empty empty-sm">
          <p class="empty-text">{t('Admin.Users.None')}</p>
        </div>
      ) : (
        <>
          <div class="table-container">
            <table class="table table-hover table-stack">
              <thead>
                <tr>
                  <th>{t('Admin.Users.User')}</th>
                  <th>{t('Field.Email')}</th>
                  <th>{t('Admin.Users.Status')}</th>
                  <th>{t('Admin.Users.Joined')}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((user) => (
                  <tr key={user.id}>
                    <td data-label={t('Admin.Users.User')}>
                      <a href={adminPaths.user(user.id)}>@{user.handle}</a>
                    </td>
                    <td data-label={t('Field.Email')}>{user.email}</td>
                    <td data-label={t('Admin.Users.Status')}>
                      <UserBadges user={user} />
                    </td>
                    <td data-label={t('Admin.Users.Joined')}>
                      <Time value={user.createdAt} />
                    </td>
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
