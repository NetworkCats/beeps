// A menu (<details>): it opens without script, as a plain <details>; drawn here it also closes on a click
// elsewhere or on Escape, as people expect from a menu. Its owner may hold its state, to close it after an action.
import type { ComponentChildren } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';

export interface MenuProps {
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
      if (!element.current?.contains(event.target as Node)) setOpen(false);
    };
    const key = (event: KeyboardEvent): void => {
      if (event.key !== 'Escape') return;
      setOpen(false);
      element.current?.querySelector('summary')?.focus();
    };
    document.addEventListener('click', click);
    document.addEventListener('keydown', key);
    return () => {
      document.removeEventListener('click', click);
      document.removeEventListener('keydown', key);
    };
  }, [open]);

  return (
    <details class={className} open={open} ref={element} onToggle={(event) => setOpen(event.currentTarget.open)}>
      {summary}
      {children}
    </details>
  );
}
