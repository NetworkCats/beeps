// What the islands are drawn from, as the server writes it (Pages/Islands/*Props.cs). Flags that are false and values
// that are null are left out of the JSON, so they are optional here.
import type { AppConfig } from './config.ts';

/** How an island follows the site live (LiveProps). */
export interface LiveProps {
  watch?: string;
  subject?: string;
  actors?: string[];
  url?: string;
  on?: string;
}

export interface ActorProps {
  id: string;
  agent?: boolean;
  handle: string | null;
  displayName: string | null;
  avatar: string;
}

export interface MediaProps {
  url: string;
  thumbnail: string;
  width: number;
  height: number;
}

export interface BeepProps {
  id: string;
  author: ActorProps;
  createdAt: string;
  replyToId?: string;
  /** The text as the server renders it, with mentions and hashtags as links: the one place that is done. */
  html: string;
  media: MediaProps[];
  likeCount: number;
  replyCount: number;
  liked?: boolean;
  canDelete?: boolean;
  underReview?: boolean;
  mentions: string[];
}

export interface BeepListProps {
  items: BeepProps[];
  next?: string;
  emptyTitle: string;
  emptyText?: string;
}

export interface NotificationProps {
  id: string;
  type: 'mention' | 'reply' | 'like';
  from: ActorProps;
  createdAt: string;
  read?: boolean;
  /** How many more liked the beep before `from`. */
  others?: number;
  /** The newest of those others, newest first. */
  earlier?: ActorProps[];
  beep: BeepProps;
}

export interface ComposerProps {
  returnUrl: string;
  replyTo?: string;
  text?: string;
}

export interface PageProps<T> {
  items: T[];
  next?: string;
}

/** A form's refused fields, by name; `""` holds what belongs to no single field. */
export type Errors = Record<string, string[]>;

/** One island as the server sends it: its props, and the errors of a form it shows that was just refused. */
export interface Envelope<P = unknown> {
  props: P;
  errors?: Errors;
}

/** What of the layout is the page's own (PageShell.cs): the navigation's item for it, the parts it leaves out. */
export interface PageShell {
  nav?: string;
  wide?: boolean;
  hidden: string[];
  styles: string[];
}

/** What a page's data says of it besides its islands' props (IslandPageProps): enough to show it in place of another. */
export interface PageView {
  lang: string;
  auth?: string;
  liveFrom: number;
  config: AppConfig;
  shell: PageShell;
  /** The content column, its islands empty: they are drawn from `islands`. */
  main: string;
}

/** A page's data (_Islands.cshtml). */
export interface IslandsPage {
  title: string;
  notice?: string;
  alert?: string;
  islands: Record<string, Envelope>;
  page: PageView;
}
