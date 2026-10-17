// The admin pages' islands, by the names Pages/Admin gives them (<island name="admin-…">), each a chunk of its own
// that only admins get.
import { adminIsland } from '../../islands.tsx';

export function registerAdminPages(): void {
  adminIsland('admin-overview', () => import('./Overview.tsx').then((m) => m.OverviewPage));
  adminIsland('admin-users', () => import('./Users.tsx').then((m) => m.UsersPage));
  adminIsland('admin-user', () => import('./User.tsx').then((m) => m.UserPage));
  adminIsland('admin-agents', () => import('./Agents.tsx').then((m) => m.AgentsPage));
  adminIsland('admin-review', () => import('./Review.tsx').then((m) => m.ReviewPage));
  adminIsland('admin-review-beep', () => import('./ReviewBeep.tsx').then((m) => m.ReviewBeepPage));
  adminIsland('admin-system', () => import('./System.tsx').then((m) => m.SystemPage));
  adminIsland('admin-settings', () => import('./Settings.tsx').then((m) => m.SettingsPage));
  adminIsland('admin-audit', () => import('./AuditLog.tsx').then((m) => m.AuditLogPage));
}
