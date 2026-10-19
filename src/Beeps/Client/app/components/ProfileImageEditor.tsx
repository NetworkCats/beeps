// The banner and avatar of a profile being edited, on the settings page and an agent's page, drawn as Twitter's
// "Edit profile" draws them: the banner across the column with the avatar over its lower edge, and round buttons on
// each to pick a new image or remove it. A picked file opens in a dialog with Cropper.js (loaded only then), with a
// square selection for an avatar and a 3:1 one for a banner; the selection, in pixels of the original image, goes into
// hidden fields (Crop.X / Crop.Y / Crop.Size, or BannerCrop.X / BannerCrop.Y / BannerCrop.Width). Applying it uploads
// the original file as it is and the server does the cropping, so nothing here has to be trusted. A new image shows
// once the server has processed it.
import type { ComponentChildren, TargetedEvent, TargetedSubmitEvent } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type Cropper from 'cropperjs';
import { BANNER_ASPECT, toBannerCropFields, toCropFields, type CropBox } from '../crop-fields.ts';
import { config } from '../config.ts';
import { sendForm } from '../forms.ts';
import { t } from '../i18n.ts';
import { checkImages } from '../text.ts';
import { showToast } from '../toasts.ts';
import { Antiforgery } from './Forms.tsx';
import { Icon } from './Icon.tsx';

export interface AvatarEditorProps {
  avatarUrl: string;
  hasCustomAvatar?: boolean;
  /** The page the forms post to, with its Avatar and RemoveAvatar handlers. */
  action: string;
}

export interface BannerEditorProps {
  /** None while there is no banner, and while a new one is processing. */
  bannerUrl?: string;
  /** The page the forms post to, with its Banner and RemoveBanner handlers. */
  action: string;
}

const ACCEPT = 'image/jpeg,image/png,image/webp,image/gif';

interface UploadFormProps {
  /** The file input's name, which is also what its ids start with. */
  field: 'Avatar' | 'Banner';
  action: string;
  aspectRatio: number;
  /** The hidden fields with the selection; empty before one is made. */
  cropFields: (box: CropBox | null) => ComponentChildren;
  /** What the button that picks a file says to screen readers and on hover. */
  label: string;
  cropHelp: string;
  cropImage: string;
}

/** The button that picks a file, and the dialog that crops it and sends it. */
function UploadForm(props: UploadFormProps) {
  const input = useRef<HTMLInputElement>(null);
  const dialog = useRef<HTMLDialogElement>(null);
  const image = useRef<HTMLImageElement>(null);
  const cropper = useRef<Cropper | null>(null);
  const [source, setSource] = useState<string | null>(null);
  const [box, setBox] = useState<CropBox | null>(null);
  const { mediaBytes } = config().limits;
  const titleId = `${props.field}-crop-title`;
  const helpId = `${props.field}-crop-help`;

  const reset = (): void => {
    cropper.current?.destroy();
    cropper.current = null;
    setSource((previous) => {
      if (previous) URL.revokeObjectURL(previous);
      return null;
    });
    setBox(null);
  };
  useEffect(() => reset, []);
  useEffect(() => {
    if (source && !dialog.current?.open) dialog.current?.showModal();
  }, [source]);

  const picked = (event: TargetedEvent<HTMLInputElement>): void => {
    const file = event.currentTarget.files?.[0];
    reset();
    if (!file) return;
    if (checkImages([file.size], 1, mediaBytes) === 'too-large') {
      event.currentTarget.value = '';
      showToast(t('Error.MediaTooLarge', mediaBytes / (1024 * 1024)), true);
      return;
    }
    setSource(URL.createObjectURL(file));
  };

  // Cropper measures its container, so it starts once the image is shown in the open dialog.
  const loaded = async (): Promise<void> => {
    const element = image.current;
    if (!element || cropper.current) return;
    if (!dialog.current?.open) dialog.current?.showModal();
    const { default: CropperClass } = await import('cropperjs');
    if (image.current !== element || !element.getAttribute('src')) return;
    cropper.current = new CropperClass(element, {
      aspectRatio: props.aspectRatio,
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
      crop: (event) => setBox(event.detail),
    });
  };

  // Closed by applying, by its close button or by Escape: the file is let go either way.
  const closed = (): void => {
    if (input.current) input.current.value = '';
    reset();
  };

  const upload = (event: TargetedSubmitEvent<HTMLFormElement>): void => {
    event.preventDefault();
    const form = event.currentTarget;
    // Stays open while the image is on its way; what went wrong, if anything, shows on the page behind it.
    void sendForm(form, event.submitter ?? null).finally(() => dialog.current?.close());
  };

  return (
    <form class="image-upload" method="post" enctype="multipart/form-data" action={`${props.action}?handler=${props.field}`} onSubmit={upload}>
      <button class="image-action" type="button" aria-label={props.label} title={props.label} onClick={() => input.current?.click()}>
        <Icon name="add_a_photo" />
      </button>
      <input class="visually-hidden" ref={input} id={props.field} type="file" name={props.field} accept={ACCEPT} tabIndex={-1} onChange={picked} />
      <dialog class="dialog dialog-lg dialog-plain crop-dialog" ref={dialog} aria-labelledby={titleId} onClose={closed}>
        <div class="crop-dialog-header">
          <button class="column-back" type="button" aria-label={t('Confirm.Cancel')} onClick={() => dialog.current?.close()}>
            <Icon name="close" />
          </button>
          <h2 class="crop-dialog-title" id={titleId}>
            {t('Image.Edit')}
          </h2>
          <button class="btn btn-sm btn-primary" type="submit" disabled={!box}>
            {t('Image.Apply')}
          </button>
        </div>
        <div class={props.aspectRatio === 1 ? 'image-crop-stage round' : 'image-crop-stage'}>
          <img alt={props.cropImage} aria-describedby={helpId} src={source ?? undefined} ref={image} onLoad={() => void loaded()} onError={reset} />
        </div>
        <p class="help crop-dialog-help" id={helpId}>
          {props.cropHelp}
        </p>
      </dialog>
      {props.cropFields(box)}
      <Antiforgery />
    </form>
  );
}

function RemoveForm({ action, confirm, label }: { action: string; confirm: string; label: string }) {
  const remove = (event: TargetedSubmitEvent<HTMLFormElement>): void => {
    event.preventDefault();
    void sendForm(event.currentTarget, event.submitter ?? null);
  };
  return (
    <form class="image-upload" method="post" action={action} data-confirm={confirm} onSubmit={remove}>
      <button class="image-action" type="submit" aria-label={label} title={label}>
        <Icon name="close" />
      </button>
      <Antiforgery />
    </form>
  );
}

/** The banner, with the avatar over its lower edge, each with its buttons. */
export function ProfileImages({ avatar, banner }: { avatar: AvatarEditorProps; banner: BannerEditorProps }) {
  return (
    <div>
      <div class="image-editor-banner">
        {banner.bannerUrl && <img src={banner.bannerUrl} alt="" width={1500} height={500} />}
        <div class="image-actions">
          <UploadForm
            field="Banner"
            action={banner.action}
            aspectRatio={BANNER_ASPECT}
            cropFields={(box) => {
              const fields = box && toBannerCropFields(box);
              return (
                <>
                  <input type="hidden" name="BannerCrop.X" value={fields ? String(fields.x) : ''} />
                  <input type="hidden" name="BannerCrop.Y" value={fields ? String(fields.y) : ''} />
                  <input type="hidden" name="BannerCrop.Width" value={fields ? String(fields.width) : ''} />
                </>
              );
            }}
            label={t('Banner.Choose')}
            cropHelp={t('Banner.Crop.Help')}
            cropImage={t('Banner.Crop.Image')}
          />
          {banner.bannerUrl && (
            <RemoveForm action={`${banner.action}?handler=RemoveBanner`} confirm={t('Banner.Remove.Confirm')} label={t('Banner.Remove')} />
          )}
        </div>
      </div>
      <div class="image-editor-avatar">
        <span class="avatar">
          <img src={avatar.avatarUrl} alt="" width={112} height={112} />
        </span>
        <div class="image-actions">
          <UploadForm
            field="Avatar"
            action={avatar.action}
            aspectRatio={1}
            cropFields={(box) => {
              const fields = box && toCropFields(box);
              return (
                <>
                  <input type="hidden" name="Crop.X" value={fields ? String(fields.x) : ''} />
                  <input type="hidden" name="Crop.Y" value={fields ? String(fields.y) : ''} />
                  <input type="hidden" name="Crop.Size" value={fields ? String(fields.size) : ''} />
                </>
              );
            }}
            label={t('Avatar.Choose')}
            cropHelp={t('Avatar.Crop.Help')}
            cropImage={t('Avatar.Crop.Image')}
          />
          {avatar.hasCustomAvatar && (
            <RemoveForm action={`${avatar.action}?handler=RemoveAvatar`} confirm={t('Avatar.Remove.Confirm')} label={t('Avatar.Remove')} />
          )}
        </div>
      </div>
    </div>
  );
}
