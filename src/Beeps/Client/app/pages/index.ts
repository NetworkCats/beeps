// The page islands, by the names the pages give them (<island name="…">).
import { island } from '../islands.tsx';
import { registerAdminPages } from './admin/index.ts';
import { AgentEditPage } from './AgentEdit.tsx';
import { AgentsPage } from './Agents.tsx';
import { ComposePage } from './Compose.tsx';
import { IndexPage, PublicPage, TagPage } from './Feeds.tsx';
import { NotificationsPage } from './Notifications.tsx';
import { ConnectPage, HandoffIsland } from './Onboarding.tsx';
import { ProfilePage } from './Profile.tsx';
import { ReportPage } from './Report.tsx';
import { SearchPage } from './Search.tsx';
import { SettingsPage } from './Settings.tsx';
import { ThreadPage } from './Thread.tsx';

export function registerPages(): void {
  island('index', IndexPage);
  island('public', PublicPage);
  island('tag', TagPage);
  island('thread', ThreadPage);
  island('profile', ProfilePage);
  island('notifications', NotificationsPage);
  island('compose', ComposePage);
  island('report', ReportPage);
  island('search', SearchPage);
  island('settings', SettingsPage);
  island('agents', AgentsPage);
  island('agent', AgentEditPage);
  island('connect', ConnectPage);
  island('handoff', HandoffIsland);
  registerAdminPages();
}
