// One agent on its owner's agents page, with what can be done to it, and a new key while it is still the agent's.
import { sending } from '../forms.ts';
import { t } from '../i18n.ts';
import { paths } from '../paths.ts';
import { Antiforgery } from './Forms.tsx';
import { Icon } from './Icon.tsx';
import { Time } from './Time.tsx';

export interface AgentRowProps {
  id: string;
  handle?: string;
  displayName?: string;
  modelName?: string;
  avatarUrl: string;
  disabled?: boolean;
  connected?: boolean;
  keyPrefix?: string;
  keyLastUsedAt?: string;
}

/** A key just made, with the sentence that hands it to the AI. */
export interface NewKeyProps {
  agentId: string;
  keyPrefix: string;
  prompt: string;
  connected?: boolean;
}

/** A key shown is still good while the agent has it: regenerated or deleted elsewhere, it is taken down. */
export function stillGood(key: NewKeyProps, agents: readonly AgentRowProps[]): boolean {
  return agents.some((agent) => agent.id === key.agentId && agent.keyPrefix === key.keyPrefix);
}

export function AgentRow({ agent }: { agent: AgentRowProps }) {
  const action = (handler: string): string => `${paths.agents}?id=${agent.id}&handler=${handler}`;
  return (
    <li class="agent-row">
      <span class="avatar">
        <img src={agent.avatarUrl} alt="" width={40} height={40} />
      </span>
      <div class="agent-info">
        <p class="agent-name">
          {agent.displayName ?? t('Agents.Unnamed')}
          {agent.disabled ? (
            <span class="badge badge-warning">{t('Agents.Status.Disabled')}</span>
          ) : !agent.connected ? (
            <span class="badge">{t('Agents.Status.NotConnected')}</span>
          ) : null}
        </p>
        <p class="agent-meta">
          {agent.handle !== undefined && <a href={paths.profile(agent.handle)}>@{agent.handle}</a>}
          {agent.modelName !== undefined && <span>{agent.modelName}</span>}
          {agent.keyPrefix !== undefined && (
            <span class="agent-key">
              <Icon name="key" />
              <code>{agent.keyPrefix}…</code>
            </span>
          )}
          <span>
            {t('Agents.LastUsed')} {agent.keyLastUsedAt === undefined ? t('Agents.Never') : <Time value={agent.keyLastUsedAt} />}
          </span>
        </p>
      </div>
      <div class="agent-actions">
        {agent.connected && (
          <a class="btn btn-sm" href={`${paths.agents}/${agent.id}`}>
            {t('Agents.Edit')}
          </a>
        )}
        <form method="post" action={action('Regenerate')} data-confirm={t('Agents.Regenerate.Confirm')} onSubmit={sending()}>
          <button class="btn btn-sm" type="submit">
            {t('Agents.Regenerate')}
          </button>
          <Antiforgery />
        </form>
        {agent.disabled ? (
          <form method="post" action={action('Enable')} onSubmit={sending()}>
            <button class="btn btn-sm" type="submit">
              {t('Agents.Enable')}
            </button>
            <Antiforgery />
          </form>
        ) : (
          <form method="post" action={action('Disable')} onSubmit={sending()}>
            <button class="btn btn-sm" type="submit">
              {t('Agents.Disable')}
            </button>
            <Antiforgery />
          </form>
        )}
        <form method="post" action={action('Delete')} data-confirm={t('Agents.Delete.Confirm')} onSubmit={sending()}>
          <button class="btn btn-sm btn-soft-danger" type="submit">
            {t('Agents.Delete')}
          </button>
          <Antiforgery />
        </form>
      </div>
    </li>
  );
}
