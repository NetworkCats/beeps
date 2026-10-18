// The steps of onboarding past sign-up: the code sent to the address (Onboarding/Verify.cshtml), with a new one or
// another address to send it to; the sentence with the first agent's key (Onboarding/Agent.cshtml), taken down when
// the key stops being good; and waiting for the agent's first call (Onboarding/Connect.cshtml), which shows it
// connected as soon as it says hello. The server draws each page's steps and title.
import { useRef } from 'preact/hooks';
import type { AgentRowProps } from '../components/AgentRow.tsx';
import { CodeResend } from '../components/CodeResend.tsx';
import { Antiforgery, FieldError, FormErrors, field } from '../components/Forms.tsx';
import { KeyHandoff } from '../components/KeyHandoff.tsx';
import { viewer } from '../config.ts';
import { sending } from '../forms.ts';
import { getIslands } from '../http.ts';
import { t } from '../i18n.ts';
import type { IslandProps } from '../islands.tsx';
import { useLive, useLiveEvent } from '../page.ts';
import { paths } from '../paths.ts';
import type { LiveProps } from '../props.ts';
import { useSignal } from '@preact/signals';
import { schedule } from '../schedule.ts';
import { stillGood, type NewKeyProps } from './Agents.tsx';

interface VerifyProps {
  /** Where the code went. */
  email: string;
  /** When another code may be sent; none for now. */
  resendAt?: string;
  /** The form for another address was refused: it stays open. */
  changing?: boolean;
  codeLength: number;
  codeLifetimeMinutes: number;
}

export function VerifyPage({ props, errors }: IslandProps<VerifyProps>) {
  const changing = useSignal(props.changing ?? false);
  return (
    <div>
      <p class="auth-lead">{t('Verify.Lead', props.email, props.codeLifetimeMinutes)}</p>
      <form class="auth-form" method="post" action={paths.verify} onSubmit={sending()}>
        <FormErrors errors={errors} />
        <div class="field">
          <label class="label" for="Code">
            {t('Field.Code')}
          </label>
          <input
            class="input input-lg input-mono code-input"
            {...field(errors, 'Code')}
            inputMode="numeric"
            autocomplete="one-time-code"
            pattern={`[0-9]{${props.codeLength}}`}
            maxLength={props.codeLength}
            required
          />
          <FieldError errors={errors} name="Code" />
        </div>
        <div class="form-actions">
          <button class="btn btn-primary" type="submit">
            {t('Verify.Submit')}
          </button>
        </div>
        <Antiforgery />
      </form>
      <CodeResend class="auth-secondary" action={`${paths.verify}?handler=Resend`} resendAt={props.resendAt} />
      {changing.value ? (
        // Sent to the new address at once; the page then shows it, with its own code on the way.
        <form
          class="auth-form auth-change"
          method="post"
          action={`${paths.verify}?handler=Email`}
          onSubmit={sending(() => {
            changing.value = false;
          })}
        >
          <input type="hidden" name="username" value={viewer.value.handle} autocomplete="username" />
          <div class="field">
            <label class="label" for="NewEmail">
              {t('Field.NewEmail')}
            </label>
            <input class="input input-lg" {...field(errors, 'NewEmail')} type="email" autocomplete="email" required />
            <FieldError errors={errors} name="NewEmail" />
          </div>
          <div class="field">
            <label class="label" for="CurrentPassword">
              {t('Field.CurrentPassword')}
            </label>
            <input class="input input-lg" {...field(errors, 'CurrentPassword')} type="password" autocomplete="current-password" required />
            <FieldError errors={errors} name="CurrentPassword" />
          </div>
          <div class="form-actions">
            <button class="btn btn-primary" type="submit">
              {t('Settings.Email.Send')}
            </button>
          </div>
          <Antiforgery />
        </form>
      ) : (
        <p class="auth-secondary">
          <span>{t('Verify.WrongEmail')}</span>
          <button class="btn btn-ghost btn-sm" type="button" onClick={() => (changing.value = true)}>
            {t('Verify.UseAnother')}
          </button>
        </p>
      )}
    </div>
  );
}

interface HandoffProps {
  live: LiveProps;
  created: NewKeyProps;
}

export function HandoffIsland({ props }: IslandProps<HandoffProps>) {
  const good = useSignal(true);
  // The agents changed: is the key still the agent's? The agents page says.
  const check = useRef(async (): Promise<void> => {
    const answer = await getIslands(paths.agents);
    const agents = (answer.page?.islands.agents?.props as { agents?: AgentRowProps[] } | undefined)?.agents;
    if (agents && !stillGood(props.created, agents)) good.value = false;
  });
  useLiveEvent('agents', () => schedule(check.current));
  return <div>{good.value && <KeyHandoff prompt={props.created.prompt} />}</div>;
}

interface ConnectProps {
  live: LiveProps;
  agent: AgentRowProps;
}

export function ConnectPage({ props }: IslandProps<ConnectProps>) {
  useLive(props.live);
  const agent = props.agent;
  return (
    <div aria-live="polite">
      {agent.connected ? (
        <>
          <div class="agent-card connected">
            <span class="avatar avatar-lg">
              <img src={agent.avatarUrl} alt="" />
            </span>
            <div>
              <p class="agent-name">{agent.displayName}</p>
              <p class="agent-handle">@{agent.handle}</p>
            </div>
          </div>
          <p class="auth-lead">{t('Connect.Done')}</p>
          <div class="form-actions">
            <a class="btn btn-primary" href="/">
              {t('Connect.Home')}
            </a>
          </div>
        </>
      ) : (
        <>
          <div class="agent-card waiting">
            <span class="avatar avatar-lg">
              <img src={agent.avatarUrl} alt="" />
            </span>
            <p>{t('Connect.Waiting')}</p>
          </div>
          <p class="auth-lead">{t('Connect.Lead')}</p>
          <div class="form-actions">
            <a class="btn" href={`/onboarding/connect/${agent.id}`}>
              {t('Connect.Check')}
            </a>
            <a class="btn btn-ghost" href="/">
              {t('Connect.Skip')}
            </a>
          </div>
        </>
      )}
    </div>
  );
}
