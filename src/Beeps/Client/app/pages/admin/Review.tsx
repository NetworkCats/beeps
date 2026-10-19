// The review queue, oldest first (Admin/Review/Index.cshtml): beeps the AI hid, that failed review, or that were
// reported. Fetched again after any change on the site (a new report, a decision by another admin); later pages load
// as the table is scrolled.
import { Pager } from '../../components/Pager.tsx';
import { Time } from '../../components/Time.tsx';
import { t } from '../../i18n.ts';
import type { IslandProps } from '../../islands.tsx';
import { useLive } from '../../page.ts';
import type { LiveProps, PageProps } from '../../props.ts';
import { useRows } from '../../rows.ts';
import { adminPaths, ModerationStatus } from './parts.tsx';

interface QueueRow {
  beepId: string;
  text: string;
  authorHandle?: string;
  status: string;
  createdAt: string;
  openReports?: number;
  /** What the AI hid it for, localized. */
  categories: string[];
}

interface AdminReviewProps {
  live: LiveProps;
  queue: PageProps<QueueRow>;
}

export function ReviewPage({ props }: IslandProps<AdminReviewProps>) {
  useLive(props.live);
  const { rows, next, loadMore } = useRows(props, {
    island: 'admin-review',
    key: (item: QueueRow) => item.beepId,
    pick: (p) => p.queue,
    snapshot: (queue) => ({ ...props, queue }),
  });
  return (
    <>
      <header class="section-header">
        <h1>{t('Admin.Review')}</h1>
      </header>
      <p class="section-lead">{t('Admin.Review.Lead')}</p>
      {rows.length === 0 ? (
        <div class="empty empty-sm">
          <p class="empty-text">{t('Admin.Review.None')}</p>
        </div>
      ) : (
        <>
          <div class="table-container">
            <table class="table table-hover table-stack">
              <thead>
                <tr>
                  <th>{t('Admin.Review.Beep')}</th>
                  <th>{t('Admin.Review.Author')}</th>
                  <th>{t('Admin.Review.Status')}</th>
                  <th>{t('Admin.Review.Why')}</th>
                  <th>{t('Admin.Review.Posted')}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((item) => (
                  <tr key={item.beepId}>
                    <td data-label={t('Admin.Review.Beep')}>
                      <a class="review-excerpt" href={adminPaths.beep(item.beepId)}>
                        {item.text.length === 0 ? t('Admin.Review.ImagesOnly') : item.text}
                      </a>
                    </td>
                    <td data-label={t('Admin.Review.Author')}>@{item.authorHandle ?? '?'}</td>
                    <td data-label={t('Admin.Review.Status')}>
                      <ModerationStatus status={item.status} />
                    </td>
                    <td data-label={t('Admin.Review.Why')}>
                      <span class="tag-group">
                        {(item.openReports ?? 0) > 0 && <span class="badge badge-warning">{item.openReports === 1 ? t('Admin.Review.Reports.One') : t('Admin.Review.Reports', item.openReports)}</span>}
                        {item.categories.map((category, index) => (
                          <span key={index} class="badge">
                            {category}
                          </span>
                        ))}
                      </span>
                    </td>
                    <td data-label={t('Admin.Review.Posted')}>
                      <Time value={item.createdAt} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {next && <Pager key={next} href={next} label={t('Admin.Next')} small ariaLabel={t('Admin.Pager')} onMore={loadMore} />}
        </>
      )}
    </>
  );
}
