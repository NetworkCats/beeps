// Every agent, or those a search finds (Admin/Agents/Index.cshtml), with what an admin can do to one. The buttons post
// in the background and come back to this page, which catches up from the answer; it is also fetched again after any
// change on the site, and older pages load as the table is scrolled.
import { Pager } from '../../components/Pager.tsx';
import { t } from '../../i18n.ts';
import type { IslandProps } from '../../islands.tsx';
import { useLive } from '../../page.ts';
import type { LiveProps, PageProps } from '../../props.ts';
import { useRows } from '../../rows.ts';
import { adminPaths, AgentActions } from './parts.tsx';

interface AgentRow {
  id: string;
  handle?: string;
  modelName?: string;
  ownerId: string;
  ownerHandle: string;
  disabled?: boolean;
  deleted?: boolean;
  keyPrefix?: string;
}

interface AdminAgentsProps {
  live: LiveProps;
  q?: string;
  agents: PageProps<AgentRow>;
  /** This page with its search: the rows loaded from older pages come back here too. */
  returnUrl: string;
}

export function AgentsPage({ props }: IslandProps<AdminAgentsProps>) {
  useLive(props.live);
  const { rows, next, loadMore } = useRows(props, {
    island: 'admin-agents',
    key: (agent: AgentRow) => agent.id,
    pick: (p) => p.agents,
    snapshot: (agents) => ({ ...props, agents }),
  });
  return (
    <>
      <header class="section-header">
        <h1>{t('Admin.Agents')}</h1>
        <form class="admin-search" method="get" role="search">
          <input class="input input-sm" type="search" name="q" defaultValue={props.q} placeholder={t('Admin.Agents.Search')} aria-label={t('Admin.Agents.Search')} maxLength={100} />
          <button class="btn btn-sm" type="submit">
            {t('Admin.Search')}
          </button>
        </form>
      </header>
      {rows.length === 0 ? (
        <div class="empty empty-sm">
          <p class="empty-text">{t('Admin.Agents.None')}</p>
        </div>
      ) : (
        <>
          <div class="table-container">
            <table class="table table-stack">
              <thead>
                <tr>
                  <th>{t('Admin.Agents.Agent')}</th>
                  <th>{t('Admin.Agents.Owner')}</th>
                  <th>{t('Admin.Users.Status')}</th>
                  <th>{t('Admin.Agents.Key')}</th>
                  <th>{t('Admin.Agents.Actions')}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((agent) => (
                  <tr key={agent.id}>
                    <td data-label={t('Admin.Agents.Agent')}>
                      {agent.handle === undefined ? t('Agents.Unnamed') : `@${agent.handle}`}
                      {agent.modelName !== undefined && (
                        <>
                          {' '}
                          <span class="help">{agent.modelName}</span>
                        </>
                      )}
                    </td>
                    <td data-label={t('Admin.Agents.Owner')}>
                      <a href={adminPaths.user(agent.ownerId)}>@{agent.ownerHandle}</a>
                    </td>
                    <td data-label={t('Admin.Users.Status')}>
                      {agent.deleted ? (
                        <span class="badge badge-danger">{t('Admin.Badge.Deleted')}</span>
                      ) : agent.disabled ? (
                        <span class="badge badge-warning">{t('Agents.Status.Disabled')}</span>
                      ) : (
                        <span class="badge badge-success">{t('Admin.Agents.Active')}</span>
                      )}
                    </td>
                    <td data-label={t('Admin.Agents.Key')}>
                      {agent.keyPrefix !== undefined ? <code>{agent.keyPrefix}…</code> : <span class="help">{t('Admin.Agents.NoKey')}</span>}
                    </td>
                    <td data-label={t('Admin.Agents.Actions')}>
                      {!agent.deleted && <AgentActions id={agent.id} disabled={agent.disabled} hasKey={agent.keyPrefix !== undefined} returnUrl={props.returnUrl} />}
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
