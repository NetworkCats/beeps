// Reporting a beep to the admins (Beep/Report.cshtml): a reason and an optional note. Sent, it goes back to the beep.
import type { TargetedSubmitEvent } from 'preact';
import { useMemo } from 'preact/hooks';
import { putBeeps } from '../beeps.ts';
import { BeepCard } from '../components/BeepCard.tsx';
import { Antiforgery, FieldError, FormErrors, field } from '../components/Forms.tsx';
import { Icon } from '../components/Icon.tsx';
import { sendForm } from '../forms.ts';
import { t } from '../i18n.ts';
import type { IslandProps } from '../islands.tsx';
import { useLive } from '../page.ts';
import { paths } from '../paths.ts';
import type { BeepProps, LiveProps } from '../props.ts';

interface ReportProps {
  live: LiveProps;
  beep: BeepProps;
  reasons: Array<{ value: string; label: string }>;
  reason?: string;
  note?: string;
  noteMaxLength: number;
}

const REASON = 'Reason';
const NOTE = 'Note';

export function ReportPage({ props, errors }: IslandProps<ReportProps>) {
  useLive(props.live);
  useMemo(() => putBeeps([props.beep]), [props.beep]);
  const permalink = paths.beep(props.beep.id);
  const submit = (event: TargetedSubmitEvent<HTMLFormElement>): void => {
    event.preventDefault();
    void sendForm(event.currentTarget, event.submitter ?? null);
  };
  return (
    <section class="feed">
      <header class="column-header">
        <a class="column-back" href={permalink} aria-label={t('Nav.Back')}>
          <Icon name="arrow_back" />
        </a>
        <h1 class="column-title">{t('Report.Title')}</h1>
      </header>
      <p class="column-lead">{t('Report.Lead')}</p>
      <FormErrors errors={errors} />
      <BeepCard id={props.beep.id} />
      <form class="report-form settings-section" method="post" onSubmit={submit}>
        <fieldset class="check-group">
          <legend class="label">{t('Report.Reason')}</legend>
          {props.reasons.map((reason) => (
            <label class="radio" key={reason.value}>
              <input type="radio" name={REASON} value={reason.value} defaultChecked={props.reason === reason.value} required />
              <span>{reason.label}</span>
            </label>
          ))}
          <FieldError errors={errors} name={REASON} />
        </fieldset>
        <div class="field">
          <label class="label" for={NOTE}>
            {t('Report.Note')}
          </label>
          <textarea class="textarea" {...field(errors, NOTE)} rows={3} maxLength={props.noteMaxLength} defaultValue={props.note ?? ''} />
          <p class="help">{t('Report.Note.Help', props.noteMaxLength)}</p>
          <FieldError errors={errors} name={NOTE} />
        </div>
        <div class="form-actions">
          <a class="btn" href={permalink}>
            {t('Report.Cancel')}
          </a>
          <button class="btn btn-primary" type="submit">
            {t('Report.Submit')}
          </button>
        </div>
        <Antiforgery />
      </form>
    </section>
  );
}
