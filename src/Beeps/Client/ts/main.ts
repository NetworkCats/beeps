// Site-wide entry, loaded on every page. Modules find their elements by tag, class or data-*
// attribute and initialise themselves; pages never call into this code.
import { initTime } from './time.ts';

initTime();
