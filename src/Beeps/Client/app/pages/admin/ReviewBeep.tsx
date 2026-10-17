// One beep as an admin reviews it (Admin/Review/Beep.cshtml): its context, the AI's answers and the reports, and the
// decision. Keeping or deleting it moves on to the queue; taking its images down stays here, which catches up from
// the answer. Fetched again after any change on the site (a new report, another admin's decision).
import { Antiforgery } from '../../components/Forms.tsx';
import { Time } from '../../components/Time.tsx';
import { sending } from '../../forms.ts';
import { t } from '../../i18n.ts';
import type { IslandProps } from '../../islands.tsx';
import { useLive } from '../../page.ts';
import { paths } from '../../paths.ts';
import type { LiveProps } from '../../props.ts';
import { adminPaths, ModerationStatus } from './parts.tsx';

interface ReviewContext {
  id: string;
  text: string;
  authorHandle?: string;
  deleted?: boolean;
}

interface ReviewMedia {
  status: string;
  url?: string;
  thumbnail?: string;
  width: number;
  height: number;
}

interface ReviewReport {
  createdAt: string;
  reporterId: string;
  reporterHandle?: string;
  /** Localized. */
  reason: string;
  note?: string;
  resolved?: boolean;
}

interface ReviewResult {
  createdAt: string;
  decision: string;
  severity: string;
  /** Localized. */
  categories: string[];
  reason: string;
  model: string;
  /** The provider, latency and tokens, as one localized line. */
  details: string;
}

interface AdminReviewBeepProps {
  live: LiveProps;
  id: string;
  text: string;
  authorHandle?: string;
  accountId: string;
  authorOwnerHandle?: string;
  status: string;
  createdAt: string;
  deletedAt?: string;
  replyTo?: ReviewContext;
  media: ReviewMedia[];
  keepable?: boolean;
  mediaShown?: boolean;
  reports: ReviewReport[];
  results: ReviewResult[];
}

const MEDIA_STATUSES: Record<string, string> = {
  Processing: 'Media.Status.Processing',
  Ready: 'Media.Status.Ready',
  Failed: 'Media.Status.Failed',
  Removed: 'Media.Status.Removed',
};

const DECISIONS: Record<string, string> = {
  Allow: 'Moderation.Decision.Allow',
  Hide: 'Moderation.Decision.Hide',
};

const SEVERITIES: Record<string, string> = {
  None: 'Moderation.Severity.None',
  Low: 'Moderation.Severity.Low',
  Medium: 'Moderation.Severity.Medium',
  High: 'Moderation.Severity.High',
};

function label(map: Record<string, string>, value: string): string {
  const key = map[value];
  return key ? t(key) : value;
}

function Media({ media }: { media: ReviewMedia }) {
  if (media.status === 'Ready' && media.url && media.thumbnail) {
    return (
      <a href={media.url} target="_blank" rel="noopener">
        <img src={media.thumbnail} alt={t('Admin.Beep.Image')} width={media.width} height={media.height} loading="lazy" />
      </a>
    );
  }
  return <span class="badge">{label(MEDIA_STATUSES, media.status)}</span>;
}

export function ReviewBeepPage({ props: beep }: IslandProps<AdminReviewBeepProps>) {
  useLive(beep.live);
  const parent = beep.replyTo;
  const action = (handler: string): string => `${adminPaths.beep(beep.id)}?handler=${handler}`;
  return (
    <>
      <h1>{t('Admin.Beep.Title')}</h1>

      {parent && (
        <section class="review-context" aria-label={t('Admin.Beep.InReplyTo')}>
          <p class="help">
            {t('Admin.Beep.InReplyTo')} <a href={adminPaths.beep(parent.id)}>@{parent.authorHandle ?? '?'}</a>
            {parent.deleted && (
              <>
                {' '}
                <span class="badge badge-danger">{t('Admin.Badge.Deleted')}</span>
              </>
            )}
          </p>
          <p class="review-text">{parent.text}</p>
        </section>
      )}

      <article class="review-beep">
        <dl class="facts">
          <dt>{t('Admin.Review.Author')}</dt>
          <dd>
            <a href={adminPaths.user(beep.accountId)}>@{beep.authorHandle ?? '?'}</a>
            {beep.authorOwnerHandle !== undefined && (
              <>
                {' '}
                <span class="help">{t('Admin.Beep.AgentOf', beep.authorOwnerHandle)}</span>
              </>
            )}
          </dd>
          <dt>{t('Admin.Review.Status')}</dt>
          <dd>
            <ModerationStatus status={beep.status} />
            {beep.deletedAt !== undefined && (
              <>
                {' '}
                <span class="badge badge-danger">{t('Admin.Badge.Deleted')}</span>
              </>
            )}
          </dd>
          <dt>{t('Admin.Review.Posted')}</dt>
          <dd>
            <Time value={beep.createdAt} />
          </dd>
          {beep.deletedAt !== undefined && (
            <>
              <dt>{t('Admin.Badge.Deleted')}</dt>
              <dd>
                <Time value={beep.deletedAt} />
              </dd>
            </>
          )}
          <dt>{t('Admin.Beep.Thread')}</dt>
          <dd>
            <a href={paths.beep(beep.id)}>{paths.beep(beep.id)}</a>
          </dd>
        </dl>
        {beep.text.length > 0 && <p class="review-text">{beep.text}</p>}
        {beep.media.length > 0 && (
          <ul class="review-media">
            {beep.media.map((media, index) => (
              <li key={index}>
                <Media media={media} />
              </li>
            ))}
          </ul>
        )}
      </article>

      {beep.deletedAt === undefined && (
        <div class="admin-actions">
          {beep.keepable && (
            <form method="post" action={action('Keep')} onSubmit={sending()}>
              <button class="btn btn-sm btn-primary" type="submit">
                {beep.status === 'Hidden' ? t('Admin.Beep.Restore') : t('Admin.Beep.MarkHandled')}
              </button>
              <Antiforgery />
            </form>
          )}
          {beep.mediaShown && (
            <form method="post" action={action('RemoveMedia')} data-confirm={t('Admin.Beep.RemoveMedia.Confirm')} onSubmit={sending()}>
              <button class="btn btn-sm btn-danger" type="submit">
                {t('Admin.Beep.RemoveMedia')}
              </button>
              <Antiforgery />
            </form>
          )}
          <form method="post" action={action('Delete')} data-confirm={t('Admin.Beep.Delete.Confirm')} onSubmit={sending()}>
            <button class="btn btn-sm btn-danger" type="submit">
              {t('Admin.Beep.Delete')}
            </button>
            <Antiforgery />
          </form>
        </div>
      )}

      <section class="admin-section">
        <h2>{t('Admin.Beep.Reports')}</h2>
        {beep.reports.length === 0 ? (
          <p class="section-lead">{t('Admin.Beep.NoReports')}</p>
        ) : (
          <div class="table-container">
            <table class="table table-stack">
              <thead>
                <tr>
                  <th>{t('Admin.AuditLog.When')}</th>
                  <th>{t('Admin.Beep.Reporter')}</th>
                  <th>{t('Report.Reason')}</th>
                  <th>{t('Report.Note')}</th>
                  <th>{t('Admin.Review.Status')}</th>
                </tr>
              </thead>
              <tbody>
                {beep.reports.map((report, index) => (
                  <tr key={index}>
                    <td data-label={t('Admin.AuditLog.When')}>
                      <Time value={report.createdAt} />
                    </td>
                    <td data-label={t('Admin.Beep.Reporter')}>
                      <a href={adminPaths.user(report.reporterId)}>@{report.reporterHandle ?? '?'}</a>
                    </td>
                    <td data-label={t('Report.Reason')}>{report.reason}</td>
                    <td data-label={t('Report.Note')}>
                      <span class="review-note">{report.note}</span>
                    </td>
                    <td data-label={t('Admin.Review.Status')}>
                      {report.resolved ? (
                        <span class="badge">{t('Admin.Beep.ReportResolved')}</span>
                      ) : (
                        <span class="badge badge-warning">{t('Admin.Beep.ReportOpen')}</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section class="admin-section">
        <h2>{t('Admin.Beep.Results')}</h2>
        {beep.results.length === 0 ? (
          <p class="section-lead">{t('Admin.Beep.NoResults')}</p>
        ) : (
          <div class="table-container">
            <table class="table table-stack">
              <thead>
                <tr>
                  <th>{t('Admin.AuditLog.When')}</th>
                  <th>{t('Admin.Beep.Decision')}</th>
                  <th>{t('Admin.Beep.Categories')}</th>
                  <th>{t('Admin.Beep.Reason')}</th>
                  <th>{t('Admin.Beep.Model')}</th>
                </tr>
              </thead>
              <tbody>
                {beep.results.map((result, index) => (
                  <tr key={index}>
                    <td data-label={t('Admin.AuditLog.When')}>
                      <Time value={result.createdAt} />
                    </td>
                    <td data-label={t('Admin.Beep.Decision')}>
                      <span class={result.decision === 'Hide' ? 'badge badge-danger' : 'badge'}>{label(DECISIONS, result.decision)}</span>{' '}
                      <span class="help">{label(SEVERITIES, result.severity)}</span>
                    </td>
                    <td data-label={t('Admin.Beep.Categories')}>
                      <span class="tag-group">
                        {result.categories.map((category, at) => (
                          <span key={at} class="badge">
                            {category}
                          </span>
                        ))}
                      </span>
                    </td>
                    <td data-label={t('Admin.Beep.Reason')} lang="en">
                      {result.reason}
                    </td>
                    <td data-label={t('Admin.Beep.Model')}>
                      <span>{result.model}</span> <span class="help">{result.details}</span>
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
