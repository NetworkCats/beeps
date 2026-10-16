// Avatar cropping on the settings and agent pages (<form data-avatar-cropper>). Picking a file shows it in
// Cropper.js with a square selection; the selection, in pixels of the original image, goes into the hidden
// Crop.X / Crop.Y / Crop.Size fields. The original file is uploaded as it is and the server does the cropping,
// so nothing here has to be trusted. Without this script the fields stay empty and the server uses the center.
import Cropper from 'cropperjs';
import { type CropFields, toCropFields } from './avatar-crop-fields.ts';

function initForm(form: HTMLFormElement): void {
  const input = form.querySelector<HTMLInputElement>('input[type="file"]');
  const area = form.querySelector<HTMLElement>('[data-avatar-crop-area]');
  const image = form.querySelector<HTMLImageElement>('[data-avatar-crop-image]');
  const fieldX = form.querySelector<HTMLInputElement>('[data-avatar-crop-x]');
  const fieldY = form.querySelector<HTMLInputElement>('[data-avatar-crop-y]');
  const fieldSize = form.querySelector<HTMLInputElement>('[data-avatar-crop-size]');
  if (!input || !area || !image || !fieldX || !fieldY || !fieldSize) return;

  let cropper: Cropper | null = null;
  let objectUrl: string | null = null;

  const write = (fields: CropFields | null): void => {
    fieldX.value = fields ? String(fields.x) : '';
    fieldY.value = fields ? String(fields.y) : '';
    fieldSize.value = fields ? String(fields.size) : '';
  };

  const reset = (): void => {
    cropper?.destroy();
    cropper = null;
    if (objectUrl) URL.revokeObjectURL(objectUrl);
    objectUrl = null;
    image.removeAttribute('src');
    area.hidden = true;
    write(null);
  };

  input.addEventListener('change', () => {
    reset();
    const file = input.files?.[0];
    if (!file) return;
    objectUrl = URL.createObjectURL(file);
    image.addEventListener(
      'load',
      () => {
        // Cropper measures its container, so it must be visible first.
        area.hidden = false;
        cropper = new Cropper(image, {
          aspectRatio: 1,
          viewMode: 1,
          autoCropArea: 1,
          dragMode: 'move',
          background: false,
          // Browsers already show photos upright by their EXIF orientation, and the server crops in that same
          // upright space; Cropper must not turn them a second time.
          checkOrientation: false,
          rotatable: false,
          scalable: false,
          toggleDragModeOnDblclick: false,
          crop: (event) => write(toCropFields(event.detail)),
        });
      },
      { once: true },
    );
    // A file the browser cannot show (or no image at all): no cropper; the server decides and explains.
    image.addEventListener('error', () => reset(), { once: true });
    image.src = objectUrl;
  });
}

for (const form of document.querySelectorAll<HTMLFormElement>('form[data-avatar-cropper]')) initForm(form);
