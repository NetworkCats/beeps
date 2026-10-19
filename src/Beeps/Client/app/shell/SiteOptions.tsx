// The site's own links and settings (Pages/Shared/_SiteOptions.cshtml), in the footer and in the top bar's or the
// navigation rail's "more" menu.
// A language takes effect as soon as it is picked (the page is shown anew in it). A colour scheme switches the page in
// place and is saved in the background: in the cookie, and signed in to the account, whose other tabs then switch too.
// Signed out the forms are GETs, so cached public pages carry no antiforgery token.
import type { TargetedEvent, TargetedSubmitEvent } from 'preact';
import { Antiforgery } from '../components/Forms.tsx';
import { Icon } from '../components/Icon.tsx';
import { Menu } from '../components/Menu.tsx';
import { t } from '../i18n.ts';
import type { IslandProps } from '../islands.tsx';
import { paths } from '../paths.ts';
import { pagePath } from '../router.ts';
import { theme } from '../state.ts';

interface OptionProps {
  value: string;
  label: string;
  lang?: string;
}

interface SiteOptionsProps {
  menu?: boolean;
  rail?: boolean;
  signedIn?: boolean;
  returnUrl: string;
  culture: string;
  cultures: OptionProps[];
  themes: OptionProps[];
  year: number;
}

function Options({ props }: { props: SiteOptionsProps }) {
  const method = props.signedIn ? 'post' : 'get';
  // The layout's islands stay as other pages are shown in place: the page to come back to is the one shown now.
  const returnUrl = pagePath.value ?? props.returnUrl;

  const submitOnChange = (event: TargetedEvent<HTMLSelectElement>): void => {
    event.currentTarget.form?.requestSubmit();
  };

  const switchTheme = (event: TargetedSubmitEvent<HTMLFormElement>): void => {
    event.preventDefault();
    const form = event.currentTarget;
    const data = new FormData(form);
    theme.value = String(data.get('theme') ?? 'system');
    // The answer is a redirect back here; only what it stores matters. Offline, the choice lasts until the next page.
    const query = new URLSearchParams([...data].map(([name, value]) => [name, String(value)]));
    const sent = props.signedIn
      ? fetch(form.action, { method: 'POST', body: data, credentials: 'same-origin', redirect: 'manual' })
      : fetch(`${form.action}?${query}`, { credentials: 'same-origin', redirect: 'manual' });
    void sent.catch(() => {});
  };

  return (
    <>
      <a class="footer-link" href={paths.about}>
        {t('Footer.About')}
      </a>
      <form class="footer-switch" method={method} action={paths.culture}>
        <input type="hidden" name="returnUrl" value={returnUrl} />
        <Icon name="language" />
        <select class="footer-select" name="culture" aria-label={t('Footer.Language')} onChange={submitOnChange}>
          {props.cultures.map((option) => (
            <option key={option.value} value={option.value} lang={option.lang} selected={option.value === props.culture}>
              {option.label}
            </option>
          ))}
        </select>
        <button class="footer-apply" type="submit">
          {t('Footer.Apply')}
        </button>
        <Antiforgery />
      </form>
      <form class="footer-switch" method={method} action={paths.theme} onSubmit={switchTheme}>
        <input type="hidden" name="returnUrl" value={returnUrl} />
        <Icon name="contrast" />
        <select class="footer-select" name="theme" aria-label={t('Footer.Theme')} onChange={submitOnChange}>
          {props.themes.map((option) => (
            <option key={option.value} value={option.value} selected={option.value === theme.value}>
              {option.label}
            </option>
          ))}
        </select>
        <button class="footer-apply" type="submit">
          {t('Footer.Apply')}
        </button>
        <Antiforgery />
      </form>
      <p class="footer-note">
        © {props.year} {t('Site.Name')}
      </p>
    </>
  );
}

export function SiteOptions({ props }: IslandProps<SiteOptionsProps>) {
  if (!props.menu) return <Options props={props} />;
  return (
    <Menu
      class={props.rail ? 'menu sidenav-more' : 'menu'}
      summary={
        props.rail ? (
          <summary class="sidenav-item" aria-label={t('Nav.More')}>
            <Icon name="more_horiz" />
            <span class="sidenav-label">{t('Nav.More')}</span>
          </summary>
        ) : (
          <summary class="topbar-more-toggle" aria-label={t('Nav.More')}>
            <Icon name="more_horiz" />
          </summary>
        )
      }
    >
      <div class="dropdown-menu menu-panel site-menu">
        <Options props={props} />
      </div>
    </Menu>
  );
}
