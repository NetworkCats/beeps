// The signed-in person's menu (Pages/Shared/_AccountMenu.cshtml): in the navigation rail on wide screens, in the top
// bar on narrow ones. Their avatar shows as soon as a new one is processed.
import { Antiforgery } from '../components/Forms.tsx';
import { Icon } from '../components/Icon.tsx';
import { Menu } from '../components/Menu.tsx';
import { viewer } from '../config.ts';
import { t } from '../i18n.ts';
import type { IslandProps } from '../islands.tsx';
import { paths } from '../paths.ts';

export function AccountMenu({ props }: IslandProps<{ rail?: boolean }>) {
  const { handle, avatar, admin } = viewer.value;
  const picture = (
    <span class="avatar">
      <img src={avatar ?? undefined} alt="" width={32} height={32} />
    </span>
  );
  return (
    <Menu
      class={props.rail ? 'menu sidenav-menu' : 'menu topbar-menu'}
      summary={
        <summary class="menu-toggle" aria-label={t('Nav.Account')}>
          {picture}
          {props.rail && <span class="menu-handle">@{handle}</span>}
          {props.rail && <Icon name="more_horiz" class="menu-more" />}
        </summary>
      }
    >
      <div class="dropdown-menu menu-panel">
        <p class="menu-header">@{handle}</p>
        <a class="dropdown-item" href={paths.profile(handle)}>
          <Icon name="person" />
          {t('Nav.Profile')}
        </a>
        <a class="dropdown-item" href={paths.settings}>
          <Icon name="settings" />
          {t('Nav.Settings')}
        </a>
        {admin && (
          <a class="dropdown-item" href="/admin">
            <Icon name="admin_panel_settings" />
            {t('Nav.Admin')}
          </a>
        )}
        <hr class="dropdown-divider" />
        {/* Sent as a page: signed out, the next page needs the app anew. */}
        <form method="post" action="/logout">
          <button class="dropdown-item" type="submit">
            <Icon name="logout" />
            {t('Nav.Logout')}
          </button>
          <Antiforgery />
        </form>
      </div>
    </Menu>
  );
}
