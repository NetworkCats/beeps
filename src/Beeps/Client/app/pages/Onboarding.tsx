// The last steps of onboarding: the sentence with the first agent's key (Onboarding/Agent.cshtml), taken down when
// the key stops being good, and waiting for the agent's first call (Onboarding/Connect.cshtml), which shows it
// connected as soon as it says hello.
import { useRef } from 'preact/hooks';
import type { AgentRowProps } from '../components/AgentRow.tsx';
import { KeyHandoff } from '../components/KeyHandoff.tsx';
import { OnboardingSteps } from '../components/OnboardingSteps.tsx';
import { getIslands } from '../http.ts';
import { t } from '../i18n.ts';
import type { IslandProps } from '../islands.tsx';
import { useLive, useLiveEvent } from '../page.ts';
import { paths } from '../paths.ts';
import type { LiveProps } from '../props.ts';
import { useSignal } from '@preact/signals';
import { schedule } from '../schedule.ts';
import { stillGood, type NewKeyProps } from './Agents.tsx';

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
    <section class="auth">
      <OnboardingSteps current={5} />
      <h1>{t('Connect.Title')}</h1>
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
    </section>
  );
}
