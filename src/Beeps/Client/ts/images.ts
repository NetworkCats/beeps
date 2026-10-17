// Image pickers (<input type="file" data-images data-max-files data-max-bytes data-too-many data-too-large>):
// too many or too large files are flagged as soon as they are chosen, with the server's own message. The
// server checks everything again (type, size, dimensions); this only saves a long upload that would fail.

export type ImagesProblem = 'too-many' | 'too-large' | null;

export function checkImages(sizes: readonly number[], maxFiles: number, maxBytes: number): ImagesProblem {
  if (maxFiles > 0 && sizes.length > maxFiles) return 'too-many';
  if (maxBytes > 0 && sizes.some((size) => size > maxBytes)) return 'too-large';
  return null;
}

function validate(input: HTMLInputElement): void {
  const sizes = Array.from(input.files ?? [], (file) => file.size);
  const problem = checkImages(sizes, Number(input.dataset.maxFiles), Number(input.dataset.maxBytes));
  const message = problem === 'too-many' ? input.dataset.tooMany : problem === 'too-large' ? input.dataset.tooLarge : '';
  input.setCustomValidity(message ?? '');
  if (message) input.reportValidity();
}

// The composer shows the picked images small (<div data-image-previews> in the same form), so a picker that is
// only an icon still says what will be posted.
function preview(input: HTMLInputElement): void {
  const box = input.form?.querySelector<HTMLElement>('[data-image-previews]');
  if (!box) return;
  for (const image of box.querySelectorAll('img')) URL.revokeObjectURL(image.src);
  box.replaceChildren(
    ...Array.from(input.files ?? [], (file) => Object.assign(document.createElement('img'), { src: URL.createObjectURL(file), alt: file.name })),
  );
  box.hidden = box.childElementCount === 0;
}

export function initImages(): void {
  document.addEventListener('change', (event) => {
    if (!(event.target instanceof HTMLInputElement) || !event.target.matches('input[type="file"][data-images]')) return;
    validate(event.target);
    preview(event.target);
  });
}
