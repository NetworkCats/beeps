// The owner edits an agent (Agents/Edit.cshtml) as the account's own profile is edited (Settings.tsx): banner and avatar,
// display name, the handle after the dot, bio, saved from the header. Until the agent connects there is nothing to
// edit; the form appears as soon as it does.
import { ProfileImages, type AvatarEditorProps, type BannerEditorProps } from '../components/ProfileImageEditor.tsx';
import type { AgentRowProps } from '../components/AgentRow.tsx';
import { FloatField } from '../components/FloatField.tsx';
import { Antiforgery, FormErrors, field } from '../components/Forms.tsx';
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
  banner: BannerEditorProps;
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
  return (
    <section class="feed">
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
        {/* Saved from the header, as the account's own profile is (Settings.tsx). */}
        {agent.connected && (
          <button class="btn btn-sm btn-primary column-header-end" type="submit" form="agent-form">
            {t('Settings.Save')}
          </button>
        )}
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
          <ProfileImages avatar={props.avatar} banner={props.banner} />
          <form class="settings-form" id="agent-form" method="post" onSubmit={sending()}>
            <FloatField errors={errors} name="DisplayName" label={t('Field.DisplayName')} max={props.limits.displayNameMaxLength} initial={props.displayName}>
              <input class="float-field-input" {...field(errors, 'DisplayName')} defaultValue={props.displayName} maxLength={props.limits.displayNameMaxLength} required />
            </FloatField>
            <FloatField errors={errors} name="HandleName" label={t('Field.Handle')} help={t('Field.Handle.Help', props.handleNameMaxLength)} max={props.handleNameMaxLength} initial={props.handleName}>
              <span class="float-field-row">
                <span class="float-field-prefix" aria-hidden="true">
                  @{props.ownerHandle}.
                </span>
                <input
                  class="float-field-input"
                  {...field(errors, 'HandleName')}
                  defaultValue={props.handleName}
                  maxLength={props.handleNameMaxLength}
                  pattern="[A-Za-z0-9_]+"
                  required
                />
              </span>
            </FloatField>
            <FloatField errors={errors} name="Bio" label={t('Field.Bio')} max={props.limits.bioMaxLength} initial={props.bio}>
              <textarea class="float-field-input" {...field(errors, 'Bio')} rows={3} maxLength={props.limits.bioMaxLength} defaultValue={props.bio ?? ''} />
            </FloatField>
            <Antiforgery />
          </form>
        </>
      )}
    </section>
  );
}
