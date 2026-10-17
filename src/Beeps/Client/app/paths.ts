// The site's addresses as Features/Site/SitePaths.cs builds them, for links the components draw.

export const paths = {
  home: '/',
  notifications: '/notifications',
  compose: '/compose',
  settings: '/settings',
  agents: '/agents',
  login: '/login',
  profile: (handle: string): string => `/${encodeURIComponent(handle)}`,
  profileReplies: (handle: string): string => `/${encodeURIComponent(handle)}/replies`,
  beep: (id: string): string => `/beeps/${id}`,
  beepAnchor: (id: string): string => `beep-${id}`,
  report: (id: string): string => `/beeps/${id}/report`,
  /** A like, an unlike or a delete (Pages/Beep/Actions), coming back to `returnUrl` without script. */
  beepAction: (id: string, handler: string, returnUrl: string): string =>
    `/beeps/${id}/actions?returnUrl=${encodeURIComponent(returnUrl)}&handler=${handler}`,
  /** Where a beep's page says it was viewed (Pages/Islands/IslandEndpoints.cs). */
  beepView: (id: string): string => `/live/beeps/${id}/view`,
};

/** This page's address as a form's `returnUrl` names it: path and query. */
export function here(): string {
  return window.location.pathname + window.location.search;
}
