// The posting form, for members. It counts what is left in the characters people see (one Chinese character or emoji
// is one), posts with Ctrl/⌘+Enter, takes images picked, dropped on it or pasted, shows them small and flags too many or too large ones at once. It posts in the background: the page it was written on catches up with the new beep,
// and a refused post shows why next to the form, with the text kept. Mentions show in colour as they are typed (drawn
// on a copy of the text behind the textarea, whose own text is transparent), and one being typed is offered the people
// it may mean, under its line, as on Twitter.
import type { ComponentChildren, TargetedClipboardEvent, TargetedDragEvent, TargetedEvent, TargetedKeyboardEvent, TargetedSubmitEvent } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import { config, viewer } from '../config.ts';
import { sendForm } from '../forms.ts';
import { getJson } from '../http.ts';
import { t } from '../i18n.ts';
import { completeMention, mentionAt, mentionRanges, type MentionAt } from '../mention.ts';
import { paths } from '../paths.ts';
import type { ActorProps, ComposerProps, Errors } from '../props.ts';
import { checkImages, countCharacters, IMAGE_TYPES, isImageType } from '../text.ts';
import { Antiforgery, FieldError, field } from './Forms.tsx';
import { Icon } from './Icon.tsx';

const IMAGES_FIELD = 'Images';
const TEXT_FIELD = 'text';
const LIVE_MENTIONS = '/live/mentions';
/** Asked once typing pauses this long, not at every letter. */
const MENTION_DELAY_MS = 150;

interface Preview {
  src: string;
  name: string;
}

/** The people offered for the mention being typed. */
interface Suggestions {
  people: ActorProps[];
  active: number;
}

/** How far down the textarea's box the line with `index` ends, measured on a copy of its text drawn out of sight. */
function lineBottom(textarea: HTMLTextAreaElement, index: number): number {
  const style = getComputedStyle(textarea);
  const copy = document.createElement('div');
  const copied = ['fontFamily', 'fontSize', 'fontStyle', 'fontWeight', 'letterSpacing', 'lineHeight', 'wordSpacing', 'tabSize', 'paddingTop', 'paddingRight', 'paddingLeft'] as const;
  for (const name of copied) copy.style[name] = style[name];
  Object.assign(copy.style, {
    position: 'absolute',
    visibility: 'hidden',
    boxSizing: 'border-box',
    width: `${textarea.clientWidth}px`,
    whiteSpace: 'pre-wrap',
    overflowWrap: 'break-word',
  });
  copy.textContent = textarea.value.slice(0, index);
  const marker = copy.appendChild(document.createElement('span'));
  marker.textContent = '\u200b';
  document.body.appendChild(copy);
  const bottom = marker.offsetTop + marker.offsetHeight;
  copy.remove();
  return textarea.offsetTop + Math.min(textarea.clientTop + bottom - textarea.scrollTop, textarea.offsetHeight);
}

/** Whether what is being dragged is files from outside, not text moved about in the page. */
function carriesFiles(event: DragEvent): boolean {
  return event.dataTransfer?.types.includes('Files') ?? false;
}

/** The text as the layer behind the textarea draws it, mentions in colour; a last space keeps a final empty line. */
function highlighted(text: string): ComponentChildren[] {
  const parts: ComponentChildren[] = [];
  let from = 0;
  for (const { start, end } of mentionRanges(text)) {
    parts.push(text.slice(from, start), <span class="mention">{text.slice(start, end)}</span>);
    from = end;
  }
  parts.push(`${text.slice(from)} `);
  return parts;
}

export function Composer({ composer, errors }: { composer: ComposerProps; errors: Errors }) {
  const reply = !!composer.replyTo;
  const { beepLength, imagesPerBeep, mediaBytes } = config().limits;
  const [text, setText] = useState(composer.text ?? '');
  const left = beepLength - countCharacters(text);
  const [previews, setPreviews] = useState<Preview[]>([]);
  // The errors of the last post from here, when the page it posted to answered with them.
  const [ownErrors, setOwnErrors] = useState<Errors | null>(null);
  const shown = ownErrors ?? errors;
  const [suggestions, setSuggestions] = useState<Suggestions | null>(null);
  const [listTop, setListTop] = useState(0);
  const textarea = useRef<HTMLTextAreaElement>(null);
  const highlight = useRef<HTMLDivElement>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const [dropping, setDropping] = useState(false);
  // Moving onto a child of the form leaves the form's own box too, so a drag is over it while enters outnumber leaves.
  const dragDepth = useRef(0);
  const lookup = useRef<{ timer?: ReturnType<typeof setTimeout>; query?: string; answers: Map<string, ActorProps[]> }>({ answers: new Map() });
  const listId = reply ? 'reply-mentions' : 'compose-mentions';

  // The page's own errors (the compose page drawn again) take over from those of an earlier post.
  useEffect(() => setOwnErrors(null), [errors]);
  useEffect(() => () => previews.forEach((preview) => URL.revokeObjectURL(preview.src)), [previews]);
  useEffect(() => () => clearTimeout(lookup.current.timer), []);
  // The browser may have put back what was typed before (going back to the page).
  useEffect(() => setText(textarea.current?.value ?? ''), []);

  const closeSuggestions = (): void => {
    clearTimeout(lookup.current.timer);
    lookup.current.query = undefined;
    setSuggestions(null);
  };

  const offer = (mention: MentionAt, people: ActorProps[]): void => {
    if (people.length === 0 || !textarea.current) {
      setSuggestions(null);
      return;
    }
    setListTop(lineBottom(textarea.current, mention.start));
    setSuggestions({ people, active: 0 });
  };

  // What is being typed at the caret: a mention is offered its people (each prefix asked once), anything else none.
  const findMention = (element: HTMLTextAreaElement): void => {
    const mention = element.selectionStart === element.selectionEnd ? mentionAt(element.value, element.selectionStart) : null;
    if (!mention) {
      closeSuggestions();
      return;
    }
    const known = lookup.current.answers.get(mention.query.toLowerCase());
    clearTimeout(lookup.current.timer);
    lookup.current.query = mention.query;
    if (known) {
      offer(mention, known);
      return;
    }
    // Until they answer, those already offered who still fit stay.
    const typedSoFar = mention.query.toLowerCase();
    setSuggestions((shown) => {
      const people = shown?.people.filter((person) => person.handle?.toLowerCase().startsWith(typedSoFar)) ?? [];
      return people.length > 0 ? { people, active: 0 } : null;
    });
    lookup.current.timer = setTimeout(() => {
      const query = mention.query;
      getJson<{ items: ActorProps[] }>(`${LIVE_MENTIONS}?${new URLSearchParams({ q: query })}`).then(
        ({ items }) => {
          lookup.current.answers.set(query.toLowerCase(), items);
          if (lookup.current.query === query) offer(mention, items);
        },
        () => {
          if (lookup.current.query === query) setSuggestions(null);
        },
      );
    }, MENTION_DELAY_MS);
  };

  const pick = (person: ActorProps): void => {
    const element = textarea.current;
    // The mention as it is now: more of it may have been typed since these people were offered.
    const mention = element && mentionAt(element.value, element.selectionStart);
    if (!element || !mention || !person.handle) return;
    const completed = completeMention(element.value, mention, person.handle);
    element.value = completed.text;
    element.setSelectionRange(completed.caret, completed.caret);
    element.focus();
    setText(completed.text);
    closeSuggestions();
  };

  const typed = (event: TargetedEvent<HTMLTextAreaElement>): void => {
    setText(event.currentTarget.value);
    findMention(event.currentTarget);
  };

  // Moving the caret by key may move it into a mention or out of one.
  const keyUp = (event: TargetedKeyboardEvent<HTMLTextAreaElement>): void => {
    if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) findMention(event.currentTarget);
  };

  // While people are offered, the arrows go through them, Enter or Tab takes one, Escape puts them away.
  const suggestionKey = (event: TargetedKeyboardEvent<HTMLTextAreaElement>): boolean => {
    if (!suggestions || event.isComposing || event.ctrlKey || event.metaKey || event.altKey) return false;
    const count = suggestions.people.length;
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      const step = event.key === 'ArrowDown' ? 1 : count - 1;
      setSuggestions({ ...suggestions, active: (suggestions.active + step) % count });
    } else if ((event.key === 'Enter' || event.key === 'Tab') && !event.shiftKey) {
      const person = suggestions.people[suggestions.active];
      if (person) pick(person);
    } else if (event.key === 'Escape') {
      closeSuggestions();
    } else {
      return false;
    }
    event.preventDefault();
    return true;
  };

  const keyDown = (event: TargetedKeyboardEvent<HTMLTextAreaElement>): void => {
    if (suggestionKey(event)) return;
    if (event.key !== 'Enter' || !(event.ctrlKey || event.metaKey)) return;
    event.preventDefault();
    event.currentTarget.form?.requestSubmit();
  };

  // Too many or too large files are flagged as soon as they are chosen, with the server's own message.
  const showImages = (input: HTMLInputElement): void => {
    const files: File[] = Array.from(input.files ?? []);
    const problem = checkImages(files.map((file) => file.size), imagesPerBeep, mediaBytes);
    const message = problem === 'too-many'
      ? t('Error.TooManyMedia', imagesPerBeep)
      : problem === 'too-large' ? t('Error.MediaTooLarge', mediaBytes / (1024 * 1024)) : '';
    input.setCustomValidity(message);
    if (message) input.reportValidity();
    setPreviews(files.map((file) => ({ src: URL.createObjectURL(file), name: file.name })));
  };

  const picked = (event: TargetedEvent<HTMLInputElement>): void => showImages(event.currentTarget);

  // Dropped or pasted images join those already chosen, in the file input the form posts; other files are left out.
  const addImages = (files: File[]): void => {
    const input = fileInput.current;
    const images = files.filter((file) => isImageType(file.type));
    if (!input || images.length === 0) return;
    const all = new DataTransfer();
    for (const file of [...Array.from(input.files ?? []), ...images]) all.items.add(file);
    input.files = all.files;
    showImages(input);
  };

  const dragEnter = (event: TargetedDragEvent<HTMLFormElement>): void => {
    if (!carriesFiles(event)) return;
    event.preventDefault();
    dragDepth.current++;
    setDropping(true);
  };

  // Without this the browser would not let the files be dropped here.
  const dragOver = (event: TargetedDragEvent<HTMLFormElement>): void => {
    if (!carriesFiles(event)) return;
    event.preventDefault();
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
  };

  const dragLeave = (event: TargetedDragEvent<HTMLFormElement>): void => {
    if (!carriesFiles(event)) return;
    dragDepth.current = Math.max(0, dragDepth.current - 1);
    if (dragDepth.current === 0) setDropping(false);
  };

  // Taken here rather than opened by the browser in place of the page, even when none of the files is an image.
  const drop = (event: TargetedDragEvent<HTMLFormElement>): void => {
    if (!carriesFiles(event)) return;
    event.preventDefault();
    dragDepth.current = 0;
    setDropping(false);
    addImages(Array.from(event.dataTransfer?.files ?? []));
  };

  // A pasted image (a screenshot, an image copied from a page) is attached. A copied selection with text in it, as Word
  // or a spreadsheet gives along with a picture of it, pastes as its text.
  const pasted = (event: TargetedClipboardEvent<HTMLTextAreaElement>): void => {
    const clipboard = event.clipboardData;
    if (!clipboard || clipboard.files.length === 0) return;
    if (clipboard.types.includes('text/plain') && clipboard.types.includes('text/html')) return;
    const images = Array.from(clipboard.files).filter((file) => isImageType(file.type));
    if (images.length === 0) return;
    event.preventDefault();
    addImages(images);
  };

  const submit = (event: TargetedSubmitEvent<HTMLFormElement>): void => {
    event.preventDefault();
    const element = event.currentTarget;
    const sentText = textarea.current?.value;
    const sentFiles = Array.from(fileInput.current?.files ?? []);
    void sendForm(element, event.submitter ?? null, {
      answeredBy: 'compose',
      onErrors: (answer) => setOwnErrors(answer),
      onDone: () => {
        // What was typed or added while it was on its way is the next beep, and stays; what went with this one goes.
        // Text changed within what was sent stays whole, to be looked over.
        const value = textarea.current?.value ?? '';
        const rest = sentText !== undefined && value.startsWith(sentText) ? value.slice(sentText.length).trimStart() : value;
        const added = Array.from(fileInput.current?.files ?? []).filter((file) => !sentFiles.includes(file));
        element.reset();
        setText('');
        setPreviews([]);
        if (rest && textarea.current) {
          textarea.current.value = rest;
          setText(rest);
        }
        addImages(added);
        setOwnErrors(null);
        closeSuggestions();
      },
    });
  };

  const action = composer.replyTo ? paths.composeReply(composer.replyTo) : paths.compose;
  return (
    <form
      class="composer"
      method="post"
      enctype="multipart/form-data"
      action={action}
      id={reply ? 'reply' : 'compose'}
      data-dropping={dropping ? '' : undefined}
      onSubmit={submit}
      onDragEnter={dragEnter}
      onDragOver={dragOver}
      onDragLeave={dragLeave}
      onDrop={drop}
    >
      <input type="hidden" name="ReturnUrl" value={composer.returnUrl} />
      <span class="avatar composer-avatar" aria-hidden="true">
        <img src={viewer.value.avatar ?? undefined} alt="" width={40} height={40} />
      </span>
      <div class="composer-main">
        <div class="field composer-field">
          <label class="label visually-hidden" for={TEXT_FIELD}>
            {t(reply ? 'Compose.ReplyLabel' : 'Compose.Label')}
          </label>
          <div class="composer-input">
            <div class="composer-text composer-highlight" ref={highlight} aria-hidden="true">
              {highlighted(text)}
            </div>
            <textarea
              class="textarea composer-text"
              {...field(shown, TEXT_FIELD)}
              ref={textarea}
              rows={reply ? 1 : 2}
              placeholder={t(reply ? 'Compose.ReplyPlaceholder' : 'Compose.Placeholder')}
              defaultValue={composer.text ?? ''}
              aria-autocomplete="list"
              aria-controls={suggestions ? listId : undefined}
              aria-activedescendant={suggestions ? `${listId}-${suggestions.active}` : undefined}
              onInput={typed}
              onKeyDown={keyDown}
              onKeyUp={keyUp}
              onPaste={pasted}
              onClick={(event) => findMention(event.currentTarget)}
              onBlur={closeSuggestions}
              onScroll={(event) => {
                if (highlight.current) highlight.current.scrollTop = event.currentTarget.scrollTop;
              }}
            />
          </div>
          {suggestions && (
            <ul class="dropdown-menu menu-panel mention-list" id={listId} role="listbox" aria-label={t('Compose.Mentions')} style={{ top: `${listTop}px` }}>
              {suggestions.people.map((person, index) => (
                <li
                  key={person.id}
                  id={`${listId}-${index}`}
                  class="mention-option"
                  role="option"
                  aria-selected={index === suggestions.active ? 'true' : 'false'}
                  // Taken on the press, before the textarea would lose its focus (and the list with it).
                  onMouseDown={(event) => {
                    event.preventDefault();
                    pick(person);
                  }}
                  onMouseMove={() => index !== suggestions.active && setSuggestions({ ...suggestions, active: index })}
                >
                  <span class="mention-name">{person.displayName ?? person.handle}</span>
                  {person.agent && <span class="badge">{t('Beep.Agent')}</span>}
                  <span class="mention-handle">@{person.handle}</span>
                </li>
              ))}
            </ul>
          )}
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
            <input class="composer-file" ref={fileInput} type="file" name={IMAGES_FIELD} multiple accept={IMAGE_TYPES} onChange={picked} />
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
