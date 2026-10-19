// The owner's agents (Agents/Index.cshtml): create, regenerate a key, disable or enable, delete. The list follows the
// agents live (connected, renamed, a new avatar, changed in another tab); a new key stays shown while it is still good.
import { useRef } from 'preact/hooks';
import { AgentRow, stillGood, type AgentRowProps, type NewKeyProps } from '../components/AgentRow.tsx';
import { Antiforgery, FormErrors } from '../components/Forms.tsx';
import { Icon } from '../components/Icon.tsx';
import { KeyHandoff } from '../components/KeyHandoff.tsx';
import { t } from '../i18n.ts';
import type { IslandProps } from '../islands.tsx';
import { useLive } from '../page.ts';
import { paths } from '../paths.ts';
import type { LiveProps } from '../props.ts';
import { sending } from '../forms.ts';

interface AgentsProps {
  live: LiveProps;
  agents: AgentRowProps[];
  newKey?: NewKeyProps;
}

export function AgentsPage({ props, errors }: IslandProps<AgentsProps>) {
  useLive(props.live);
  // Shown only in the response that made it, then kept as the list catches up, while the agent still has that key.
  const shownKey = useRef(props.newKey);
  if (props.newKey) shownKey.current = props.newKey;
  else if (shownKey.current && !stillGood(shownKey.current, props.agents)) shownKey.current = undefined;
  const key = shownKey.current;
  return (
    <section class="feed">
      <header class="column-header">
        <h1 class="column-title column-heading">{t('Agents.Title')}</h1>
        <form class="column-actions" method="post" action={`${paths.agents}?handler=Create`} onSubmit={sending()}>
          <button class="btn btn-primary btn-sm" type="submit">
            <Icon name="add" />
            {t('Agents.Create')}
          </button>
          <Antiforgery />
        </form>
      </header>
      <p class="column-lead">{t('Agents.Lead')}</p>
      <FormErrors errors={errors} />

      {key && (
        <div class="card new-key">
          <div class="card-body">
            <h2 class="card-title">{t('Agents.NewKey')}</h2>
            <KeyHandoff prompt={key.prompt} />
            {!(props.agents.find((agent) => agent.id === key.agentId)?.connected ?? key.connected) && (
              <a class="btn btn-sm" href={paths.connect(key.agentId)}>
                {t('Agents.WaitForConnection')}
              </a>
            )}
          </div>
        </div>
      )}

      {props.agents.length === 0 ? (
        <div class="empty">
          <span class="empty-icon empty-icon-circle">
            <Icon name="smart_toy" />
          </span>
          <h2 class="empty-title">{t('Agents.Empty.Title')}</h2>
          <p class="empty-text">{t('Agents.Empty.Text')}</p>
        </div>
      ) : (
        <ul class="agent-list">
          {props.agents.map((agent) => (
            <AgentRow key={agent.id} agent={agent} />
          ))}
        </ul>
      )}
    </section>
  );
}
