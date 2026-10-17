// The admin area's front page (Admin/Index.cshtml): how big the site is, and what waits for attention. Like every
// admin page it is fetched again after any change on the site, and as the figures with no events of their own move.
import { Time } from '../../components/Time.tsx';
import { t } from '../../i18n.ts';
import type { IslandProps } from '../../islands.tsx';
import { useLive } from '../../page.ts';
import type { LiveProps } from '../../props.ts';
import { adminPaths } from './parts.tsx';

interface AdminOverviewProps {
  live: LiveProps;
  since: string;
  users: number;
  agents: number;
  beeps: number;
  newUsers: number;
  newAgents: number;
  newBeeps: number;
  reviewQueue: number;
  moderationBacklog: number;
  outboxBacklog: number;
  deadJobs: number;
}

export function OverviewPage({ props: o }: IslandProps<AdminOverviewProps>) {
  useLive(o.live);
  return (
    <>
      <h1>{t('Admin.Overview')}</h1>

      <section class="admin-section">
        <h2>{t('Admin.Overview.Site')}</h2>
        <dl class="stats">
          <div class="stat">
            <dt>{t('Admin.Overview.Users')}</dt>
            <dd>{o.users}</dd>
            <dd class="stat-note">{t('Admin.Overview.Today', o.newUsers)}</dd>
          </div>
          <div class="stat">
            <dt>{t('Admin.Overview.Agents')}</dt>
            <dd>{o.agents}</dd>
            <dd class="stat-note">{t('Admin.Overview.Today', o.newAgents)}</dd>
          </div>
          <div class="stat">
            <dt>{t('Admin.Overview.Beeps')}</dt>
            <dd>{o.beeps}</dd>
            <dd class="stat-note">{t('Admin.Overview.Today', o.newBeeps)}</dd>
          </div>
        </dl>
        <p class="help">
          {t('Admin.Overview.TodayHelp')} <Time value={o.since} />
        </p>
      </section>

      <section class="admin-section">
        <h2>{t('Admin.Overview.Queues')}</h2>
        <dl class="stats">
          <div class={o.reviewQueue > 0 ? 'stat stat-attention' : 'stat'}>
            <dt>
              <a href={adminPaths.review}>{t('Admin.Overview.ReviewQueue')}</a>
            </dt>
            <dd>{o.reviewQueue}</dd>
          </div>
          <div class="stat">
            <dt>{t('Admin.Overview.ModerationBacklog')}</dt>
            <dd>{o.moderationBacklog}</dd>
          </div>
          <div class="stat">
            <dt>{t('Admin.Overview.OutboxBacklog')}</dt>
            <dd>{o.outboxBacklog}</dd>
          </div>
          <div class={o.deadJobs > 0 ? 'stat stat-attention' : 'stat'}>
            <dt>
              <a href={adminPaths.system}>{t('Admin.Overview.DeadJobs')}</a>
            </dt>
            <dd>{o.deadJobs}</dd>
          </div>
        </dl>
      </section>
    </>
  );
}
