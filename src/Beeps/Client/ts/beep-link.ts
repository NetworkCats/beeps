// A beep in a list opens when it is clicked anywhere that is not one of its own links, buttons or menus, as on
// other social sites. The permalink on its time stays the real link: keyboards, screen readers and pages without
// script use that. Selecting text in a beep does not open it.

const OWN_CONTROLS = 'a, button, input, textarea, select, label, summary, details, form, dialog';

export function initBeepLink(): void {
  document.documentElement.dataset.beepLinks = '';
  document.addEventListener('click', (event) => {
    if (event.defaultPrevented || event.button !== 0 || !(event.target instanceof Element)) return;
    const beep = event.target.closest<HTMLElement>('[data-beep]:not(.beep-focused)');
    if (!beep || event.target.closest(OWN_CONTROLS)) return;
    if (window.getSelection()?.toString()) return;
    const link = beep.querySelector<HTMLAnchorElement>('.beep-time');
    if (!link) return;
    if (event.metaKey || event.ctrlKey) window.open(link.href, '_blank', 'noopener');
    else window.location.assign(link.href);
  });
}
