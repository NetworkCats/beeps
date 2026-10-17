// The admin pages' islands, by the names Pages/Admin gives them (<island name="admin-…">).
import { island } from '../../islands.tsx';
import { AgentsPage } from './Agents.tsx';
import { AuditLogPage } from './AuditLog.tsx';
import { OverviewPage } from './Overview.tsx';
import { ReviewPage } from './Review.tsx';
import { ReviewBeepPage } from './ReviewBeep.tsx';
import { SettingsPage } from './Settings.tsx';
import { SystemPage } from './System.tsx';
import { UserPage } from './User.tsx';
import { UsersPage } from './Users.tsx';

export function registerAdminPages(): void {
  island('admin-overview', OverviewPage);
  island('admin-users', UsersPage);
  island('admin-user', UserPage);
  island('admin-agents', AgentsPage);
  island('admin-review', ReviewPage);
  island('admin-review-beep', ReviewBeepPage);
  island('admin-system', SystemPage);
  island('admin-settings', SettingsPage);
  island('admin-audit', AuditLogPage);
}
