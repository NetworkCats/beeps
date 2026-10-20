// The avatar and banner crops as the hidden form fields carry them (see components/ProfileImageEditor.tsx). No DOM here, so
// it can be tested in Node.

export interface CropBox {
  x: number;
  y: number;
  width: number;
  height: number;
}

interface CropFields {
  x: number;
  y: number;
  size: number;
}

interface BannerCropFields {
  x: number;
  y: number;
  width: number;
}

/** Banners are three times as wide as they are high (MediaRules.BannerAspect). */
export const BANNER_ASPECT = 3;

/** Cropper's selection as whole pixels; a 1:1 selection can still differ by a rounding pixel between its sides. */
export function toCropFields(box: CropBox): CropFields {
  return {
    x: Math.max(0, Math.round(box.x)),
    y: Math.max(0, Math.round(box.y)),
    size: Math.max(1, Math.round(Math.min(box.width, box.height))),
  };
}

/** Cropper's 3:1 selection as whole pixels; the server takes the height from the width, so a rounding pixel too tall is dropped. */
export function toBannerCropFields(box: CropBox): BannerCropFields {
  return {
    x: Math.max(0, Math.round(box.x)),
    y: Math.max(0, Math.round(box.y)),
    width: Math.max(BANNER_ASPECT, Math.round(Math.min(box.width, box.height * BANNER_ASPECT))),
  };
}
