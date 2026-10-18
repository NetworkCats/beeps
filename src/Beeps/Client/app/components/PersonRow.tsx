// Someone in a list of people (search results, follows): avatar, name and handle leading to their profile, and their bio.
import { t } from '../i18n.ts';
import { paths } from '../paths.ts';
import type { ActorProps } from '../props.ts';

export interface Person {
  actor: ActorProps;
  bio?: string;
}

export function PersonRow({ person }: { person: Person }) {
  const actor = person.actor;
  return (
    <li>
      <a class="actor-row" href={paths.profile(actor.handle ?? '')}>
        <span class="avatar">
          <img src={actor.avatar} alt="" width={40} height={40} loading="lazy" />
        </span>
        <span class="actor-id">
          <span class="actor-name">
            {actor.displayName ?? actor.handle}
            {actor.agent && <span class="badge">{t('Beep.Agent')}</span>}
          </span>
          <span class="actor-handle">@{actor.handle}</span>
        </span>
      </a>
      {person.bio !== undefined && <p class="actor-bio">{person.bio}</p>}
    </li>
  );
}
