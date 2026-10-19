// Background work (Admin/System.cshtml): dead jobs to retry or discard, and how the scheduled jobs last ran. Fetched
// again after any change on the site and as the figures with no events of their own move (AdminFiguresWorker); older
// dead jobs load as the table is scrolled.
import { Antiforgery } from '../../components/Forms.tsx';
import { Pager } from '../../components/Pager.tsx';
import { Time } from '../../components/Time.tsx';
import { sending } from '../../forms.ts';
import { t } from '../../i18n.ts';
import type { IslandProps } from '../../islands.tsx';
import { useLive } from '../../page.ts';
import type { LiveProps, PageProps } from '../../props.ts';
import { useRows } from '../../rows.ts';
import { adminPaths } from './parts.tsx';

interface DeadJob {
  id: string;
  type: string;
  attempts: number;
  lastError: string;
  failedAt: string;
}

interface ScheduledJob {
  name: string;
  lastStartedAt?: string;
  lastFinishedAt?: string;
  lastError?: string;
}

interface AdminSystemProps {
  live: LiveProps;
  deadJobs: PageProps<DeadJob>;
  scheduled: ScheduledJob[];
}

export function SystemPage({ props }: IslandProps<AdminSystemProps>) {
  useLive(props.live);
  const { rows, next, loadMore } = useRows(props, {
    island: 'admin-system',
    key: (job: DeadJob) => job.id,
    pick: (p) => p.deadJobs,
    snapshot: (deadJobs) => ({ ...props, deadJobs }),
  });
  return (
    <>
      <h1>{t('Admin.System')}</h1>

      <section class="admin-section">
        <h2>{t('Admin.System.DeadJobs')}</h2>
        <p class="section-lead">{t('Admin.System.DeadJobs.Lead')}</p>
        {rows.length === 0 ? (
          <div class="empty empty-sm">
            <p class="empty-text">{t('Admin.System.DeadJobs.None')}</p>
          </div>
        ) : (
          <>
            <div class="table-container">
              <table class="table table-stack">
                <thead>
                  <tr>
                    <th>{t('Admin.System.Type')}</th>
                    <th>{t('Admin.System.Failed')}</th>
                    <th>{t('Admin.System.Error')}</th>
                    <th>{t('Admin.Agents.Actions')}</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((job) => (
                    <tr key={job.id}>
                      <td data-label={t('Admin.System.Type')}>
                        <code>{job.type}</code> <span class="help">{job.attempts === 1 ? t('Admin.System.Attempts.One') : t('Admin.System.Attempts', job.attempts)}</span>
                      </td>
                      <td data-label={t('Admin.System.Failed')}>
                        <Time value={job.failedAt} />
                      </td>
                      <td data-label={t('Admin.System.Error')}>
                        <span class="review-note" lang="en">
                          {job.lastError}
                        </span>
                      </td>
                      <td data-label={t('Admin.Agents.Actions')}>
                        <div class="admin-actions admin-actions-inline">
                          <form method="post" action={adminPaths.rowAction(adminPaths.system, job.id, 'Retry')} onSubmit={sending()}>
                            <button class="btn btn-sm" type="submit">
                              {t('Admin.System.Retry')}
                            </button>
                            <Antiforgery />
                          </form>
                          <form
                            method="post"
                            action={adminPaths.rowAction(adminPaths.system, job.id, 'Discard')}
                            data-confirm={t('Admin.System.Discard.Confirm')}
                            onSubmit={sending()}
                          >
                            <button class="btn btn-sm btn-danger" type="submit">
                              {t('Admin.System.Discard')}
                            </button>
                            <Antiforgery />
                          </form>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {next && <Pager key={next} href={next} label={t('Admin.Older')} small ariaLabel={t('Admin.Pager')} onMore={loadMore} />}
          </>
        )}
      </section>

      <section class="admin-section">
        <h2>{t('Admin.System.Scheduled')}</h2>
        {props.scheduled.length === 0 ? (
          <p class="section-lead">{t('Admin.System.Scheduled.None')}</p>
        ) : (
          <div class="table-container">
            <table class="table table-stack">
              <thead>
                <tr>
                  <th>{t('Admin.System.Job')}</th>
                  <th>{t('Admin.System.LastStarted')}</th>
                  <th>{t('Admin.System.LastFinished')}</th>
                  <th>{t('Admin.System.Error')}</th>
                </tr>
              </thead>
              <tbody>
                {props.scheduled.map((job) => (
                  <tr key={job.name}>
                    <td data-label={t('Admin.System.Job')}>
                      <code>{job.name}</code>
                    </td>
                    <td data-label={t('Admin.System.LastStarted')}>{job.lastStartedAt !== undefined && <Time value={job.lastStartedAt} />}</td>
                    <td data-label={t('Admin.System.LastFinished')}>{job.lastFinishedAt !== undefined && <Time value={job.lastFinishedAt} />}</td>
                    <td data-label={t('Admin.System.Error')}>
                      {job.lastError === undefined ? (
                        <span class="badge badge-success">{t('Admin.System.Ok')}</span>
                      ) : (
                        <span class="review-note" lang="en">
                          {job.lastError}
                        </span>
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
