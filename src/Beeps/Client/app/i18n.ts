// Interface text, from the resource files (SharedResource.*.resx) by way of the layout's config: the keys the app
// uses are listed in Client/strings.json, and the server sends them in the page's language. `{0}`, `{1}`… are filled
// in as .NET's string.Format would.
import { config } from './config.ts';

export function format(text: string, args: readonly unknown[]): string {
  return text.replace(/\{(\d+)\}/g, (match, index: string) => {
    const value = args[Number(index)];
    return value === undefined ? match : String(value);
  });
}

export function t(key: string, ...args: unknown[]): string {
  const text = config().strings[key];
  return text === undefined ? key : format(text, args);
}
