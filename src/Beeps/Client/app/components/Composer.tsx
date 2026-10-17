// The posting form, drawn as Pages/Shared/_Composer.cshtml draws it. It counts what is left in the characters people
// see (one Chinese character or emoji is one), posts with Ctrl/⌘+Enter, shows the picked images small and flags too
// many or too large ones at once. It posts in the background: the page it was written on catches up with the new beep,
// and a refused post shows why next to the form, with the text kept.
import type { TargetedEvent, TargetedKeyboardEvent, TargetedSubmitEvent } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import { config, viewer } from '../config.ts';
import { sendForm } from '../forms.ts';
import { t } from '../i18n.ts';
import type { ComposerProps, Errors } from '../props.ts';
import { checkImages, countCharacters } from '../text.ts';
import { Antiforgery, FieldError, field } from './Forms.tsx';
import { Icon } from './Icon.tsx';

const IMAGES_FIELD = 'Images';
const TEXT_FIELD = 'text';

interface Preview {
  src: string;
  name: string;
}

export function Composer({ composer, errors }: { composer: ComposerProps; errors: Errors }) {
  const reply = !!composer.replyTo;
  const { beepLength, imagesPerBeep, mediaBytes } = config().limits;
  const [left, setLeft] = useState(() => beepLength - countCharacters(composer.text ?? ''));
  const [previews, setPreviews] = useState<Preview[]>([]);
  // The errors of the last post from here, when the page it posted to answered with them.
  const [ownErrors, setOwnErrors] = useState<Errors | null>(null);
  const shown = ownErrors ?? errors;

  // The page's own errors (the compose page drawn again) take over from those of an earlier post.
  useEffect(() => setOwnErrors(null), [errors]);
  useEffect(() => () => previews.forEach((preview) => URL.revokeObjectURL(preview.src)), [previews]);

  const typed = (event: TargetedEvent<HTMLTextAreaElement>): void => {
    setLeft(beepLength - countCharacters(event.currentTarget.value));
  };

  const keyDown = (event: TargetedKeyboardEvent<HTMLTextAreaElement>): void => {
    if (event.key !== 'Enter' || !(event.ctrlKey || event.metaKey)) return;
    event.preventDefault();
    event.currentTarget.form?.requestSubmit();
  };

  // Too many or too large files are flagged as soon as they are chosen, with the server's own message.
  const picked = (event: TargetedEvent<HTMLInputElement>): void => {
    const input = event.currentTarget;
    const files: File[] = Array.from(input.files ?? []);
    const problem = checkImages(files.map((file) => file.size), imagesPerBeep, mediaBytes);
    const message = problem === 'too-many'
      ? t('Error.TooManyMedia', imagesPerBeep)
      : problem === 'too-large' ? t('Error.MediaTooLarge', mediaBytes / (1024 * 1024)) : '';
    input.setCustomValidity(message);
    if (message) input.reportValidity();
    setPreviews(files.map((file) => ({ src: URL.createObjectURL(file), name: file.name })));
  };

  const submit = (event: TargetedSubmitEvent<HTMLFormElement>): void => {
    event.preventDefault();
    const element = event.currentTarget;
    void sendForm(element, event.submitter ?? null, {
      answeredBy: 'compose',
      onErrors: (answer) => setOwnErrors(answer),
      onDone: () => {
        element.reset();
        setLeft(beepLength);
        setPreviews([]);
        setOwnErrors(null);
      },
    });
  };

  const action = composer.replyTo ? `/compose?replyTo=${composer.replyTo}` : '/compose';
  return (
    <form class="composer" method="post" enctype="multipart/form-data" action={action} id={reply ? 'reply' : 'compose'} onSubmit={submit}>
      <input type="hidden" name="ReturnUrl" value={composer.returnUrl} />
      <span class="avatar composer-avatar" aria-hidden="true">
        <img src={viewer.value.avatar ?? undefined} alt="" width={40} height={40} />
      </span>
      <div class="composer-main">
        <div class="field">
          <label class="label visually-hidden" for={TEXT_FIELD}>
            {t(reply ? 'Compose.ReplyLabel' : 'Compose.Label')}
          </label>
          <textarea
            class="textarea composer-text"
            {...field(shown, TEXT_FIELD)}
            rows={reply ? 1 : 2}
            placeholder={t(reply ? 'Compose.ReplyPlaceholder' : 'Compose.Placeholder')}
            defaultValue={composer.text ?? ''}
            onInput={typed}
            onKeyDown={keyDown}
          />
          <FieldError errors={shown} name={TEXT_FIELD} />
        </div>
        <div class="composer-previews" hidden={previews.length === 0}>
          {previews.map((preview) => (
            <img key={preview.src} src={preview.src} alt={preview.name} />
          ))}
        </div>
        <div class="composer-footer">
          <label class="composer-images" title={t('Compose.Images', imagesPerBeep)}>
            <Icon name="image" />
            <span class="visually-hidden">{t('Compose.Images', imagesPerBeep)}</span>
            <input class="composer-file" type="file" name={IMAGES_FIELD} multiple accept="image/jpeg,image/png,image/webp,image/gif" onChange={picked} />
          </label>
          {/* Only near the limit is the number worth reading out. */}
          <span class="composer-count" aria-live="polite" hidden={left > beepLength * 0.2} data-over={left < 0 ? '' : undefined}>
            {left > beepLength * 0.2 ? '' : String(left)}
          </span>
          <button class="btn btn-primary" type="submit">
            {t(reply ? 'Compose.Reply' : 'Compose.Submit')}
          </button>
        </div>
        <FieldError errors={shown} name={IMAGES_FIELD} />
      </div>
      <Antiforgery />
    </form>
  );
}
