// The page islands, by the names the pages give them (<island name="…">). Each page's component is a chunk of its own,
// loaded when a page first has it (islands.tsx).
import { lazyIsland } from '../islands.tsx';
import { registerAdminPages } from './admin/index.ts';

export function registerPages(): void {
  lazyIsland('index', () => import('./Feeds.tsx').then((m) => m.IndexPage));
  lazyIsland('public', () => import('./Feeds.tsx').then((m) => m.PublicPage));
  lazyIsland('tag', () => import('./Feeds.tsx').then((m) => m.TagPage));
  lazyIsland('thread', () => import('./Thread.tsx').then((m) => m.ThreadPage));
  lazyIsland('profile', () => import('./Profile.tsx').then((m) => m.ProfilePage));
  lazyIsland('search', () => import('./Search.tsx').then((m) => m.SearchPage));
  lazyIsland('notifications', () => import('./Notifications.tsx').then((m) => m.NotificationsPage));
  lazyIsland('compose', () => import('./Compose.tsx').then((m) => m.ComposePage));
  lazyIsland('report', () => import('./Report.tsx').then((m) => m.ReportPage));
  lazyIsland('settings', () => import('./Settings.tsx').then((m) => m.SettingsPage));
  lazyIsland('agents', () => import('./Agents.tsx').then((m) => m.AgentsPage));
  lazyIsland('agent', () => import('./AgentEdit.tsx').then((m) => m.AgentEditPage));
  lazyIsland('verify', () => import('./Onboarding.tsx').then((m) => m.VerifyPage));
  lazyIsland('connect', () => import('./Onboarding.tsx').then((m) => m.ConnectPage));
  lazyIsland('handoff', () => import('./Onboarding.tsx').then((m) => m.HandoffIsland));
  registerAdminPages();
}
