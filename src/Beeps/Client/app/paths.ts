// The site's addresses as Features/Site/SitePaths.cs builds them, for links the components draw.

export const paths = {
  home: '/',
  public: '/public',
  about: '/about',
  search: '/search',
  notifications: '/notifications',
  compose: '/compose',
  /** The compose page for a reply to the beep. */
  composeReply: (id: string): string => `/compose?replyTo=${id}`,
  settings: '/settings',
  settingsSection: (section: string): string => `/settings/${section}`,
  agents: '/agents',
  admin: '/admin',
  login: '/login',
  logout: '/logout',
  /** Where the language and the colour scheme are picked (Culture.cshtml, Theme.cshtml). */
  culture: '/culture',
  theme: '/theme',
  verify: '/onboarding/verify',
  /** Waiting for an agent's first call. */
  connect: (agentId: string): string => `/onboarding/connect/${agentId}`,
  forgotPassword: '/forgot-password',
  profile: (handle: string): string => `/@${encodeURIComponent(handle)}`,
  /** A page of a profile (Features/Site/SitePaths.cs's ProfileTabs): `replies`, `likes`, `following` or `followers`. */
  profileTab: (handle: string, tab: string): string => `/@${encodeURIComponent(handle)}/${tab}`,
  beep: (id: string): string => `/beeps/${id}`,
  beepAnchor: (id: string): string => `beep-${id}`,
  report: (id: string): string => `/beeps/${id}/report`,
  /** A like, an unlike or a delete (Pages/Beep/Actions), coming back to `returnUrl` without script. */
  beepAction: (id: string, handler: string, returnUrl: string): string =>
    `/beeps/${id}/actions?returnUrl=${encodeURIComponent(returnUrl)}&handler=${handler}`,
  /** Where a beep's page says it was viewed (Pages/Islands/IslandEndpoints.cs). */
  beepView: (id: string): string => `/live/beeps/${id}/view`,
  /** Where a page says which beeps it had on screen, for the notifications they read (Pages/Islands/IslandEndpoints.cs). */
  notificationsSeen: '/live/notifications/seen',
};

/** This page's address as a form's `returnUrl` names it: path and query. */
export function here(): string {
  return window.location.pathname + window.location.search;
}
