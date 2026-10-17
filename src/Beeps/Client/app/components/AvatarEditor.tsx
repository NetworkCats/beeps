// The avatar upload and removal forms, on the settings page and an agent's page.
// Picking a file shows it in Cropper.js (loaded only then) with a square selection; the selection, in pixels of the
// original image, goes into the hidden Crop.X / Crop.Y / Crop.Size fields. The original file is uploaded as it is and
// the server does the cropping, so nothing here has to be trusted. A new avatar shows once the server has processed it.
import type { TargetedEvent, TargetedSubmitEvent } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type Cropper from 'cropperjs';
import { toCropFields, type CropFields } from '../avatar-crop-fields.ts';
import { config } from '../config.ts';
import { sendForm } from '../forms.ts';
import { t } from '../i18n.ts';
import { checkImages } from '../text.ts';
import { Antiforgery } from './Forms.tsx';

export interface AvatarEditorProps {
  avatarUrl: string;
  hasCustomAvatar?: boolean;
  /** The page the forms post to, with its Avatar and RemoveAvatar handlers. */
  action: string;
}

const FIELD = 'Avatar';

export function AvatarEditor({ editor }: { editor: AvatarEditorProps }) {
  const image = useRef<HTMLImageElement>(null);
  const cropper = useRef<Cropper | null>(null);
  const [source, setSource] = useState<string | null>(null);
  const [fields, setFields] = useState<CropFields | null>(null);
  const { mediaBytes } = config().limits;
  const megabytes = mediaBytes / (1024 * 1024);

  const reset = (): void => {
    cropper.current?.destroy();
    cropper.current = null;
    setSource((previous) => {
      if (previous) URL.revokeObjectURL(previous);
      return null;
    });
    setFields(null);
  };
  useEffect(() => reset, []);

  const picked = (event: TargetedEvent<HTMLInputElement>): void => {
    const input = event.currentTarget;
    reset();
    const file = input.files?.[0];
    if (!file) return;
    const problem = checkImages([file.size], 1, mediaBytes);
    const message = problem === 'too-large' ? t('Error.MediaTooLarge', megabytes) : '';
    input.setCustomValidity(message);
    if (message) {
      input.reportValidity();
      return;
    }
    setSource(URL.createObjectURL(file));
  };

  // Cropper measures its container, so it starts once the image is shown.
  const loaded = async (): Promise<void> => {
    const element = image.current;
    if (!element || cropper.current) return;
    const { default: CropperClass } = await import('cropperjs');
    if (image.current !== element || !element.getAttribute('src')) return;
    cropper.current = new CropperClass(element, {
      aspectRatio: 1,
      viewMode: 1,
      autoCropArea: 1,
      dragMode: 'move',
      background: false,
      // Browsers already show photos upright by their EXIF orientation, and the server crops in that same upright
      // space; Cropper must not turn them a second time.
      checkOrientation: false,
      rotatable: false,
      scalable: false,
      toggleDragModeOnDblclick: false,
      crop: (event) => setFields(toCropFields(event.detail)),
    });
  };

  const upload = (event: TargetedSubmitEvent<HTMLFormElement>): void => {
    event.preventDefault();
    const form = event.currentTarget;
    void sendForm(form, event.submitter ?? null, {
      onDone: () => {
        form.reset();
        reset();
      },
    });
  };

  const remove = (event: TargetedSubmitEvent<HTMLFormElement>): void => {
    event.preventDefault();
    void sendForm(event.currentTarget, event.submitter ?? null);
  };

  return (
    <section class="settings-section" aria-labelledby="avatar-title">
      <h2 id="avatar-title">{t('Avatar.Title')}</h2>
      <div class="avatar-editor">
        <span class="avatar avatar-2xl">
          <img src={editor.avatarUrl} alt="" width={96} height={96} />
        </span>
        <div class="avatar-editor-forms">
          <form method="post" enctype="multipart/form-data" action={`${editor.action}?handler=Avatar`} onSubmit={upload}>
            <div class="field">
              <label class="label" for={FIELD}>
                {t('Avatar.Choose')}
              </label>
              <input class="file" id={FIELD} type="file" name={FIELD} required accept="image/jpeg,image/png,image/webp,image/gif" onChange={picked} />
              <p class="help">{t('Avatar.Help', megabytes)}</p>
            </div>
            {/* The browser only picks the square; without script the fields stay empty and the server takes the centered square. */}
            <div class="avatar-crop" hidden={!source}>
              <p class="help" id="avatar-crop-help">
                {t('Avatar.Crop.Help')}
              </p>
              <div class="avatar-crop-stage">
                <img alt={t('Avatar.Crop.Image')} aria-describedby="avatar-crop-help" src={source ?? undefined} ref={image} onLoad={() => void loaded()} onError={reset} />
              </div>
            </div>
            <input type="hidden" name="Crop.X" value={fields ? String(fields.x) : undefined} />
            <input type="hidden" name="Crop.Y" value={fields ? String(fields.y) : undefined} />
            <input type="hidden" name="Crop.Size" value={fields ? String(fields.size) : undefined} />
            <div class="form-actions">
              <button class="btn" type="submit">
                {t('Avatar.Upload')}
              </button>
            </div>
            <Antiforgery />
          </form>
          <div class="avatar-remove">
            {editor.hasCustomAvatar && (
              <form method="post" action={`${editor.action}?handler=RemoveAvatar`} data-confirm={t('Avatar.Remove.Confirm')} onSubmit={remove}>
                <button class="btn btn-ghost" type="submit">
                  {t('Avatar.Remove')}
                </button>
                <Antiforgery />
              </form>
            )}
          </div>
        </div>
      </div>
    </section>
  );
}
