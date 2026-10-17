// The unread count by "Notifications" in the navigation, as the live connection last said it.
import { unread } from '../state.ts';

export function InboxCount() {
  const count = unread.value;
  return (
    <span class="count count-primary nav-count" hidden={count <= 0}>
      {count <= 0 ? '' : count > 99 ? '99+' : String(count)}
    </span>
  );
}
