// The parts of a form the server's tag helpers draw (Pages/TagHelpers/FormErrorTagHelpers.cs), and the antiforgery
// field Razor adds to every form that posts.
import { config } from '../config.ts';
import type { Errors } from '../props.ts';

export function Antiforgery() {
  const { field, token } = config().antiforgery;
  return field ? <input name={field} type="hidden" value={token} /> : null;
}

/** A field's messages. Field names are matched ignoring case, as the server's ModelState matches them. */
export function messagesFor(errors: Errors, name: string): string[] {
  const key = Object.keys(errors).find((field) => field.toLowerCase() === name.toLowerCase());
  return key === undefined ? [] : (errors[key] ?? []);
}

/** <field-error name="…" />: the field's messages, only when there are any (a whole field turns red for an .error). */
export function FieldError({ errors, name }: { errors: Errors; name: string }) {
  const messages = messagesFor(errors, name);
  if (!messages?.length) return null;
  return (
    <p class="error" id={`${name}-error`}>
      {messages.join(' ')}
    </p>
  );
}

/** <form-errors />: what belongs to no single field, as an alert. */
export function FormErrors({ errors }: { errors: Errors }) {
  const messages = errors[''];
  if (!messages?.length) return null;
  return (
    <div class="alert alert-danger" role="alert">
      <p class="alert-text">{messages.join(' ')}</p>
    </div>
  );
}

/** What beeps-field="…" adds to an input: its name and id, and aria for its errors. */
interface FieldAttributes {
  name: string;
  id: string;
  'aria-invalid'?: 'true';
  'aria-describedby'?: string;
}

export function field(errors: Errors, name: string): FieldAttributes {
  const invalid = messagesFor(errors, name).length > 0;
  return {
    name,
    id: name,
    'aria-invalid': invalid ? 'true' : undefined,
    'aria-describedby': invalid ? `${name}-error` : undefined,
  };
}

/** A count as the server draws it: nothing for none, as on other social sites. */
export function count(n: number): string {
  return n > 0 ? String(n) : '';
}
