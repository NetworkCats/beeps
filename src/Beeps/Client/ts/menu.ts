// <details data-menu>: a menu that opens without script (it is a plain <details>); this closes it on a
// click elsewhere or on Escape, as people expect from a menu.

export function initMenu(): void {
  const openMenus = (): NodeListOf<HTMLDetailsElement> => document.querySelectorAll<HTMLDetailsElement>('details[data-menu][open]');
  document.addEventListener('click', (event) => {
    for (const menu of openMenus()) {
      if (!menu.contains(event.target as Node)) menu.open = false;
    }
  });
  document.addEventListener('keydown', (event) => {
    if (event.key !== 'Escape') return;
    for (const menu of openMenus()) {
      menu.open = false;
      menu.querySelector('summary')?.focus();
    }
  });
}
