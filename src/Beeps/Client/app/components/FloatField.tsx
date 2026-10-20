// A field as Twitter draws one in its settings: the label small at the top inside the box, the input under it, and,
// while it is being typed in, how much of its length is used. The input itself is the caller's (an <input>, a
// <textarea> or a <select> with the float-field-input class), so it keeps its own attributes.
import type { ComponentChildren } from 'preact';
import { useLayoutEffect, useRef, useState } from 'preact/hooks';
import { FieldError, messagesFor } from './Forms.tsx';
import type { Errors } from '../props.ts';

interface FloatFieldProps {
  errors: Errors;
  /** The input's name and id. */
  name: string;
  label: string;
  help?: string;
  /** The input's maxLength, to count against. */
  max?: number;
  /** What the input starts with, for the count. */
  initial?: string;
  children: ComponentChildren;
}

export function FloatField({ errors, name, label, help, max, initial = '', children }: FloatFieldProps) {
  const [length, setLength] = useState(initial.length);
  const box = useRef<HTMLLabelElement>(null);
  // Props that come again (saved here, or changed in another tab) may change what the input holds, unless it was typed
  // in: counted from the input itself, as it is now.
  useLayoutEffect(() => {
    const input = box.current?.querySelector<HTMLInputElement>('.float-field-input');
    setLength(input ? input.value.length : initial.length);
  }, [initial]);
  const invalid = messagesFor(errors, name).length > 0;
  return (
    <div class={invalid ? 'float-field float-field-invalid' : 'float-field'}>
      <label
        class="float-field-box"
        for={name}
        ref={box}
        onInput={max === undefined ? undefined : (event) => setLength((event.target as HTMLInputElement).value.length)}
      >
        <span class="float-field-top">
          <span class="float-field-label">{label}</span>
          {max !== undefined && (
            <span class="float-field-count" aria-hidden="true">
              {length} / {max}
            </span>
          )}
        </span>
        {children}
      </label>
      {help && <p class="help">{help}</p>}
      <FieldError errors={errors} name={name} />
    </div>
  );
}
