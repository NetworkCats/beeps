// Site-wide entry, loaded on every page. Modules find their elements by tag, class or data-*
// attribute and initialise themselves; pages never call into this code.
import { initCompose } from './compose.ts';
import { initConfirm } from './confirm.ts';
import { initCopy } from './copy.ts';
import { initLike } from './like.ts';
import { initMenu } from './menu.ts';
import { initTime } from './time.ts';

initTime();
initCopy();
// Before the like handler: a cancelled confirmation must stop any submit.
initConfirm();
initLike();
initCompose();
initMenu();
