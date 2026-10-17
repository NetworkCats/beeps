// The timelines: home (Index.cshtml, members), the introduction with the public timeline (Index.cshtml, signed out or
// not yet verified), public (Public.cshtml) and a hashtag (Tags/Index.cshtml).
import { BeepListView, useBeepFeed, useFeedSnapshot, type FeedSource } from '../components/BeepFeed.tsx';
import { Composer } from '../components/Composer.tsx';
import { Icon } from '../components/Icon.tsx';
import { viewer } from '../config.ts';
import { t } from '../i18n.ts';
import type { IslandProps } from '../islands.tsx';
import { useLive } from '../page.ts';
import type { BeepListProps, ComposerProps, IslandsPage, LiveProps } from '../props.ts';

interface IndexProps {
  live: LiveProps;
  home?: boolean;
  composer?: ComposerProps;
  introArt?: string;
  list: BeepListProps;
}

interface PublicProps {
  live: LiveProps;
  list: BeepListProps;
}

interface TagProps extends PublicProps {
  tag: string;
}

/** The list in the answer for one of its next pages. */
function listOf(name: string) {
  return (page: IslandsPage): FeedSource | undefined => (page.islands[name]?.props as { list?: BeepListProps } | undefined)?.list;
}

function withList<P extends { list: BeepListProps }>(props: P, list: FeedSource): P {
  return { ...props, list: { ...props.list, items: list.items, next: list.next } };
}

/** Signed in, home and public are two feeds of one place: tabs in the column header switch between them (_FeedTabs.cshtml). */
function FeedTabs({ current }: { current: 'home' | 'public' }) {
  return (
    <nav class="tabs tabs-justify column-tabs" aria-label={t('Nav.Feeds')}>
      <a class="tab" href="/" aria-current={current === 'home' ? 'page' : undefined}>
        {t('Nav.Home')}
      </a>
      <a class="tab" href="/public" aria-current={current === 'public' ? 'page' : undefined}>
        {t('Nav.Public')}
      </a>
    </nav>
  );
}

export function IndexPage({ props, errors }: IslandProps<IndexProps>) {
  useLive(props.live);
  const feed = useBeepFeed({ ...props.list, watch: props.live.watch, pageOf: listOf('index') });
  useFeedSnapshot(props, feed, withList);
  if (props.home) {
    return (
      <section class="feed">
        <header class="column-header column-header-tabs">
          <h1 class="visually-hidden">{t('Home.Title')}</h1>
          <FeedTabs current="home" />
        </header>
        {props.composer && <Composer composer={props.composer} errors={errors} />}
        <BeepListView feed={feed} emptyTitle={props.list.emptyTitle} emptyText={props.list.emptyText} />
      </section>
    );
  }
  return (
    <section class="feed">
      <div class="intro">
        <img class="intro-art" src={props.introArt} alt="" width={534} height={534} />
        <div class="intro-body">
          <h1 class="intro-title">{t('Site.Name')}</h1>
          <p class="intro-text">{t('Site.Tagline')}</p>
          <a class="aside-link" href="/about">
            {t('About.LearnMore')}
          </a>
        </div>
      </div>
      <h2 class="visually-hidden">{t('Public.Title')}</h2>
      <BeepListView feed={feed} emptyTitle={props.list.emptyTitle} emptyText={props.list.emptyText} />
    </section>
  );
}

export function PublicPage({ props }: IslandProps<PublicProps>) {
  useLive(props.live);
  const feed = useBeepFeed({ ...props.list, watch: props.live.watch, pageOf: listOf('public') });
  useFeedSnapshot(props, feed, withList);
  return (
    <section class="feed">
      {viewer.value.human ? (
        <header class="column-header column-header-tabs">
          <h1 class="visually-hidden">{t('Public.Title')}</h1>
          <FeedTabs current="public" />
        </header>
      ) : (
        <>
          <header class="column-header">
            <h1 class="column-title">{t('Public.Title')}</h1>
          </header>
          <p class="column-lead">{t('Public.Lead')}</p>
        </>
      )}
      <BeepListView feed={feed} emptyTitle={props.list.emptyTitle} emptyText={props.list.emptyText} />
    </section>
  );
}

export function TagPage({ props }: IslandProps<TagProps>) {
  useLive(props.live);
  const feed = useBeepFeed({ ...props.list, watch: props.live.watch, pageOf: listOf('tag') });
  useFeedSnapshot(props, feed, withList);
  return (
    <section class="feed">
      <header class="column-header">
        <a class="column-back" href="/" aria-label={t('Nav.Back')}>
          <Icon name="arrow_back" />
        </a>
        <h1 class="column-title">#{props.tag}</h1>
      </header>
      <BeepListView feed={feed} emptyTitle={props.list.emptyTitle} emptyText={props.list.emptyText} />
    </section>
  );
}
