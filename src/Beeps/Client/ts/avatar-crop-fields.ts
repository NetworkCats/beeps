// The avatar crop as the hidden form fields carry it (see avatar-crop.ts). No DOM here, so it can be tested in Node.

export interface CropBox {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface CropFields {
  x: number;
  y: number;
  size: number;
}

/** Cropper's selection as whole pixels; a 1:1 selection can still differ by a rounding pixel between its sides. */
export function toCropFields(box: CropBox): CropFields {
  return {
    x: Math.max(0, Math.round(box.x)),
    y: Math.max(0, Math.round(box.y)),
    size: Math.max(1, Math.round(Math.min(box.width, box.height))),
  };
}
