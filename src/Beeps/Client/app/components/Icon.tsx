/** A Material Symbols glyph, as <icon name="…" /> draws it (IconTagHelper). The name must be in Client/icons.json. */
export function Icon({ name, class: extra }: { name: string; class?: string }) {
  return (
    <span class={extra ? `icon ${extra}` : 'icon'} aria-hidden="true">
      {name}
    </span>
  );
}
