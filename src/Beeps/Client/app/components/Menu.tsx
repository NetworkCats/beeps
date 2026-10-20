// A menu (<details>): it opens without script, as a plain <details>; drawn here it also closes on a click
// elsewhere or on Escape, as people expect from a menu, and as a link in it is followed or another page is shown: one in
// the layout stays as the page changes under it. Its owner may hold its state, to close it after an action.
import type { ComponentChildren } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import { PAGE_SHOWN } from '../router.ts';

interface MenuProps {
  class: string;
  summary: ComponentChildren;
  children: ComponentChildren;
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
}

export function Menu({ class: className, summary, children, open: controlled, onOpenChange }: MenuProps) {
  const [own, setOwn] = useState(false);
  const open = controlled ?? own;
  const setOpen = (value: boolean): void => {
    setOwn(value);
    onOpenChange?.(value);
  };
  const element = useRef<HTMLDetailsElement>(null);

  useEffect(() => {
    if (!open) return;
    const click = (event: MouseEvent): void => {
      const target = event.target as Element;
      if (!element.current?.contains(target) || target.closest('a[href]')) setOpen(false);
    };
    const key = (event: KeyboardEvent): void => {
      if (event.key !== 'Escape') return;
      setOpen(false);
      element.current?.querySelector('summary')?.focus();
    };
    const shown = (): void => setOpen(false);
    document.addEventListener('click', click);
    document.addEventListener('keydown', key);
    document.addEventListener(PAGE_SHOWN, shown);
    return () => {
      document.removeEventListener('click', click);
      document.removeEventListener('keydown', key);
      document.removeEventListener(PAGE_SHOWN, shown);
    };
  }, [open]);

  return (
    <details class={className} open={open} ref={element} onToggle={(event) => setOpen(event.currentTarget.open)}>
      {summary}
      {children}
    </details>
  );
}
