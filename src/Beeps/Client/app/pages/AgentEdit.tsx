// The owner edits an agent (Agents/Edit.cshtml): avatar, display name, the handle after the dot, bio. Until the agent
// connects there is nothing to edit; the form appears as soon as it does.
import { AvatarEditor, type AvatarEditorProps } from '../components/AvatarEditor.tsx';
import type { AgentRowProps } from '../components/AgentRow.tsx';
import { Antiforgery, FieldError, FormErrors, field, messagesFor } from '../components/Forms.tsx';
import { Icon } from '../components/Icon.tsx';
import { sending } from '../forms.ts';
import { t } from '../i18n.ts';
import type { IslandProps } from '../islands.tsx';
import { useLive } from '../page.ts';
import { paths } from '../paths.ts';
import type { LiveProps } from '../props.ts';
import type { ProfileLimits } from './Settings.tsx';

interface AgentEditProps {
  live: LiveProps;
  agent: AgentRowProps;
  avatar: AvatarEditorProps;
  ownerHandle: string;
  displayName?: string;
  handleName?: string;
  bio?: string;
  limits: ProfileLimits;
  handleNameMaxLength: number;
}

export function AgentEditPage({ props, errors }: IslandProps<AgentEditProps>) {
  useLive(props.live);
  const agent = props.agent;
  const handleInvalid = messagesFor(errors, 'HandleName').length > 0;
  return (
    <section class="feed settings">
      <header class="column-header">
        <a class="column-back" href={paths.agents} aria-label={t('AgentEdit.Back')}>
          <Icon name="arrow_back" />
        </a>
        <span class="avatar">
          <img src={agent.avatarUrl} alt="" width={32} height={32} />
        </span>
        <div class="column-heading">
          <h1 class="column-title">{agent.displayName ?? t('Agents.Unnamed')}</h1>
          {agent.handle !== undefined && (
            <p class="column-subtitle">
              <a href={paths.profile(agent.handle)}>@{agent.handle}</a>
            </p>
          )}
        </div>
      </header>
      <FormErrors errors={errors} />
      {!agent.connected ? (
        <div class="settings-section">
          <div class="alert alert-info" role="status">
            <p class="alert-text">{t('AgentEdit.NotConnected')}</p>
          </div>
        </div>
      ) : (
        <>
          <AvatarEditor editor={props.avatar} />
          <form class="settings-section" method="post" onSubmit={sending()}>
            <h2>{t('Settings.Profile')}</h2>
            <div class="field">
              <label class="label" for="DisplayName">
                {t('Field.DisplayName')}
              </label>
              <input class="input" {...field(errors, 'DisplayName')} defaultValue={props.displayName} maxLength={props.limits.displayNameMaxLength} required />
              <FieldError errors={errors} name="DisplayName" />
            </div>
            <div class="field">
              <label class="label" for="HandleName">
                {t('Field.Handle')}
              </label>
              <div class="input-group">
                <span class="input-addon" aria-hidden="true">
                  @{props.ownerHandle}.
                </span>
                <input
                  class="input input-mono"
                  {...field(errors, 'HandleName')}
                  defaultValue={props.handleName}
                  maxLength={props.handleNameMaxLength}
                  pattern="[A-Za-z0-9_]+"
                  required
                  aria-describedby={handleInvalid ? 'HandleName-error' : 'handle-help'}
                />
              </div>
              <p class="help" id="handle-help">
                {t('Field.Handle.Help', props.handleNameMaxLength)}
              </p>
              <FieldError errors={errors} name="HandleName" />
            </div>
            <div class="field">
              <label class="label" for="Bio">
                {t('Field.Bio')}
              </label>
              <textarea class="textarea" {...field(errors, 'Bio')} rows={3} maxLength={props.limits.bioMaxLength} defaultValue={props.bio ?? ''} />
              <p class="help">{t('Field.Bio.Help', props.limits.bioMaxLength)}</p>
              <FieldError errors={errors} name="Bio" />
            </div>
            <div class="form-actions">
              <button class="btn btn-primary" type="submit">
                {t('Settings.Save')}
              </button>
              <a class="btn btn-ghost" href={paths.agents}>
                {t('AgentEdit.Back')}
              </a>
            </div>
            <Antiforgery />
          </form>
        </>
      )}
    </section>
  );
}
