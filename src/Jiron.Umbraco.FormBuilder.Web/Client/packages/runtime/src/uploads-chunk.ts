/**
 * Uploads on the page (ROADMAP 6.4m, ADR-0152, ADR-0006), a chunk that only a form with a file field loads. Each file a
 * visitor chooses or drops gets a row in the field's list, and is checked before anything is sent, by its type, its size,
 * how many the field takes and its first bytes, as the server checks them; one that passes goes to the `uploads` endpoint,
 * the challenge first, with its progress. Its token goes in a hidden input of the row, which the page reads as the field's
 * answer. A file the server refuses shows why; one the site cannot take now says to try later, and one that stopped, a reset
 * among them, offers "Try again". While a file is on its way its input says so, so a check counts the field as answered; a
 * send waits for it, and stops at a file that failed, whose row takes the focus, until the visitor tries again or takes it off.
 */
import { hidesMarkup, HEAD_LENGTH, isExecutable, matchesKind } from '@jfb/logic/files';
import { shownErrors, wordsFor } from './messages';
import type { FormModel, ModelField } from './model';
import { fieldsOf, readPage } from './page';
import { contextOf } from './rules';
import { ON_ITS_WAY, type PageUploads, type UploadedFile, type UploadOptions } from './uploads';

const BUSY = 'data-jfb-busy';
const ERROR = 'data-jfb-failed';
const DROP = 'data-jfb-drop';

const extensionOf = (name: string): string => {
  const at = name.lastIndexOf('.');
  return at < 0 ? '' : name.slice(at + 1).toLowerCase();
};

/** What a field's template says of its files. */
interface Words {
  readonly uploading: string;
  readonly uploaded: string;
  readonly remove: string;
  readonly stop: string;
  readonly again: string;
  readonly stopped: string;
  readonly later: string;
  readonly kb: string;
  readonly mb: string;
}

/** Enhances each file field of the form: its drop zone, its buttons and its list of files. */
export function enhanceUploads(form: HTMLFormElement, model: FormModel, options: UploadOptions): PageUploads {
  const key = form.dataset['jfbForm'] ?? '';
  const running = new Set<Promise<void>>();
  const held = new Map<string, Map<HTMLElement, UploadedFile>>();
  const fields = fieldsOf(model).flatMap((field) => [field, ...(field.fields ?? [])]);
  const number = new Intl.NumberFormat(model.culture, { maximumFractionDigits: 1 });

  const sizeOf = (bytes: number, words: Words): string =>
    bytes < 1024 * 1024
      ? words.kb.replace('{0}', number.format(Math.max(bytes / 1024, 0.1)))
      : words.mb.replace('{0}', number.format(bytes / (1024 * 1024)));

  const say = (text: string): void => {
    const live = form.querySelector(':scope > [data-jfb-live]');
    if (live !== null) live.textContent = text;
  };

  /** The field's own words for an error, as the page words a check's. */
  async function messageOf(field: ModelField, code: string): Promise<string> {
    const reading = readPage(form, model);
    const context = contextOf(model, reading, Date.now(), form.ownerDocument.URL);
    const words = await wordsFor(model.culture);
    const [shown] = shownErrors(form, model, reading, [{ field: field.key, row: null, code }], context, words);
    return shown?.message ?? '';
  }

  /** What stops a file before it is sent, as the server would; null for one that may go. */
  async function refusalOf(
    file: File,
    input: HTMLInputElement,
    field: ModelField,
    count: number,
  ): Promise<string | null> {
    const taken = input.accept.split(',').map((each) => each.trim().replace(/^\./, '').toLowerCase());
    const extension = extensionOf(file.name);
    if (input.accept !== '' && !taken.includes(extension)) return messageOf(field, 'fileType');
    const most = field.settings?.['maxBytes'];
    if (typeof most === 'number' && file.size > most) return messageOf(field, 'fileSize');
    const files = field.settings?.['maxFiles'];
    if (typeof files === 'number' && files > 1 && count > files) return messageOf(field, 'fileCount');
    const head = new Uint8Array(await file.slice(0, HEAD_LENGTH).arrayBuffer());
    return isExecutable(head) || !matchesKind(extension, head) || hidesMarkup(extension, head)
      ? messageOf(field, 'fileType')
      : null;
  }

  for (const zone of form.querySelectorAll<HTMLElement>('.jfb-upload')) {
    const host = zone.closest<HTMLElement>('[data-jfb-field]');
    const input = zone.querySelector<HTMLInputElement>('input[type="file"]:not(.jfb-upload-camera)');
    const template = host?.querySelector<HTMLTemplateElement>('template[data-jfb-file]');
    const field = fields.find((each) => each.key === host?.dataset['jfbField']);
    const sample = template?.content.querySelector<HTMLElement>('li');
    if (!host || !input || !template || field === undefined || !sample) continue;
    const data = template.dataset;
    const words: Words = {
      uploading: data['jfbUploading'] ?? '',
      uploaded: data['jfbUploaded'] ?? '',
      remove: data['jfbRemove'] ?? '',
      stop: data['jfbStop'] ?? '',
      again: data['jfbAgain'] ?? '',
      stopped: data['jfbStopped'] ?? '',
      later: data['jfbLater'] ?? '',
      kb: data['jfbKb'] ?? '',
      mb: data['jfbMb'] ?? '',
    };
    const name = input.name;
    const mine = new Map<HTMLElement, UploadedFile>();
    held.set(name, mine);
    const single = !input.multiple;
    let list: HTMLElement | null = null;

    const listOf = (): HTMLElement => {
      if (list === null) {
        list = template.content.querySelector('ul')?.cloneNode(false) as HTMLElement;
        zone.after(list);
      }
      return list;
    };

    /** A file's row, with what its state shows. */
    function rowOf(fileName: string) {
      const row = sample?.cloneNode(true) as HTMLElement;
      const part = <T extends Element>(selector: string): T => row.querySelector<T>(selector) as T;
      const help = part<HTMLElement>('p:not([class*="error"])');
      const error = part<HTMLElement>('p[class*="error"]');
      const progress = part<HTMLProgressElement>('progress');
      const retry = part<HTMLButtonElement>('[data-jfb-retry]');
      const drop = part<HTMLButtonElement>('[data-jfb-drop-file]');
      let answer: HTMLInputElement | null = null;
      /** What the page reads as this file's answer: its token, that one is on its way, or nothing. */
      const answers = (value: string | null): void => {
        if (value === null) {
          answer?.remove();
          answer = null;
        } else {
          answer ??= row.appendChild(form.ownerDocument.createElement('input'));
          answer.type = 'hidden';
          answer.name = name;
          answer.value = value;
        }
        zone.dispatchEvent(new Event('input', { bubbles: true }));
      };
      part('strong').textContent = fileName;
      const dropName = drop.querySelector('.jfb-visually-hidden') as HTMLElement;
      (retry.querySelector('.jfb-visually-hidden') as HTMLElement).textContent = words.again.replace('{0}', fileName);
      const show = (state: 'busy' | 'done' | 'refused' | 'stopped', text = ''): void => {
        row.toggleAttribute(BUSY, state === 'busy');
        row.toggleAttribute(ERROR, state === 'refused' || state === 'stopped');
        help.hidden = state === 'refused' || state === 'stopped';
        error.hidden = !row.hasAttribute(ERROR);
        progress.hidden = state !== 'busy';
        retry.hidden = state !== 'stopped';
        dropName.textContent = (state === 'busy' ? words.stop : words.remove).replace('{0}', fileName);
        if (row.hasAttribute(ERROR)) error.textContent = text;
        else help.textContent = text;
      };
      return { row, show, answers, progress, retry, drop };
    }

    const removeRow = (row: HTMLElement): void => {
      mine.delete(row);
      row.querySelector('img')?.dispatchEvent(new Event('jfb:gone'));
      row.remove();
      if (list?.childElementCount === 0) {
        list.remove();
        list = null;
      }
    };

    /** The row of a file the server holds, with the input that posts its token. */
    function hold(built: ReturnType<typeof rowOf>, file: UploadedFile): void {
      built.answers(file.token);
      mine.set(built.row, file);
      built.show('done', sizeOf(file.size, words));
    }

    /** Sends a file, and shows how it went; a challenge that has expired is renewed once. */
    function send(built: ReturnType<typeof rowOf>, file: File, renewed = false): void {
      let resolve: () => void = () => undefined;
      const promise = new Promise<void>((done) => (resolve = done));
      running.add(promise);
      void promise.then(() => running.delete(promise));
      built.show('busy', words.uploading.replace('{0}', '0'));
      built.answers(ON_ITS_WAY);
      built.progress.value = 0;
      const request = new XMLHttpRequest();
      const stopped = (): void => {
        built.answers(null);
        built.show('stopped', words.stopped);
        say(`${file.name}: ${words.stopped}`);
        resolve();
      };
      const refused = async (code: string | undefined): Promise<void> => {
        const text = code === 'later' ? words.later : await messageOf(field as ModelField, code ?? 'fileType');
        built.answers(null);
        built.show('refused', text);
        say(`${file.name}: ${text}`);
        resolve();
      };
      request.upload.addEventListener('progress', (event) => {
        if (!event.lengthComputable) return;
        const percent = Math.min(99, Math.round((event.loaded / event.total) * 100));
        built.progress.value = percent;
        built.show('busy', words.uploading.replace('{0}', String(percent)));
      });
      request.addEventListener('load', () => {
        const status = request.status;
        const answer = (() => {
          try {
            return JSON.parse(request.responseText) as {
              token?: string;
              name?: string;
              size?: number;
              errors?: { code?: string }[];
            };
          } catch {
            return {};
          }
        })();
        if (status === 200 && answer.token !== undefined) {
          hold(built, { token: answer.token, name: answer.name ?? file.name, size: answer.size ?? file.size });
          say(words.uploaded.replace('{0}', file.name));
          resolve();
        } else if (status === 401 && !renewed) {
          void options.renew().then(() => {
            send(built, file, true);
            resolve();
          });
        } else if (status === 429 || status === 503) {
          void refused('later');
        } else if (status === 413 || status === 415) {
          void refused(answer.errors?.[0]?.code);
        } else {
          stopped();
        }
      });
      request.addEventListener('error', stopped);
      request.addEventListener('abort', () => resolve());
      built.drop.onclick = (): void => {
        request.abort();
        removeRow(built.row);
        zone.dispatchEvent(new Event('input', { bubbles: true }));
        input?.focus();
      };
      built.retry.onclick = (): void => {
        send(built, file);
        built.drop.focus();
      };
      void options.challenge().then((challenge) => {
        if (!built.row.isConnected) return resolve();
        const body = new FormData();
        if (challenge !== null) body.append('challenge', challenge);
        body.append('file', file, file.name);
        request.open('POST', `${options.api}/forms/${key}/uploads?fieldKey=${encodeURIComponent(field?.key ?? '')}`);
        request.send(body);
      });
    }

    const thumbnail = (built: ReturnType<typeof rowOf>, file: File): void => {
      if (!zone.hasAttribute('data-jfb-thumbnails') || !file.type.startsWith('image/')) return;
      const urls = form.ownerDocument.defaultView?.URL;
      if (urls?.createObjectURL === undefined) return;
      const address = urls.createObjectURL(file);
      const image = form.ownerDocument.createElement('img');
      image.alt = '';
      const icon = built.row.querySelector('.jfb-icon-file');
      // A site whose policy takes no picture from the page's own memory keeps the icon.
      image.addEventListener('error', () => image.replaceWith(icon as Node));
      image.addEventListener('jfb:gone', () => urls.revokeObjectURL(address));
      image.src = address;
      icon?.replaceWith(image);
    };

    /** Takes a file a visitor chose or dropped. */
    async function take(file: File): Promise<void> {
      if (single)
        for (const row of [...(list?.children ?? [])])
          (row as HTMLElement).querySelector<HTMLElement>('[data-jfb-drop-file]')?.click();
      const built = rowOf(file.name);
      listOf().append(built.row);
      built.answers(ON_ITS_WAY);
      built.drop.onclick = (): void => {
        removeRow(built.row);
        zone.dispatchEvent(new Event('input', { bubbles: true }));
        input?.focus();
      };
      // A file already refused or stopped holds no place in what the field takes.
      const refusal = await refusalOf(
        file,
        input as HTMLInputElement,
        field as ModelField,
        listOf().querySelectorAll(':scope > :not([data-jfb-failed])').length,
      ).catch(() => null);
      if (refusal !== null) {
        built.answers(null);
        built.show('refused', refusal);
        say(`${file.name}: ${refusal}`);
        return;
      }
      thumbnail(built, file);
      send(built, file);
    }

    const takeAll = (files: Iterable<File>): void => {
      const taking = (async () => {
        for (const file of files) await take(file);
      })();
      running.add(taking);
      void taking.finally(() => running.delete(taking));
    };

    for (const chooser of zone.querySelectorAll<HTMLInputElement>('input[type="file"]')) {
      chooser.addEventListener('change', () => {
        const chosen = [...(chooser.files ?? [])];
        chooser.value = '';
        takeAll(chosen);
      });
    }

    if (zone.hasAttribute('data-jfb-dropzone')) {
      const over = (event: DragEvent): void => {
        if (input.disabled) return;
        event.preventDefault();
        zone.setAttribute(DROP, '');
      };
      zone.addEventListener('dragenter', over);
      zone.addEventListener('dragover', over);
      zone.addEventListener('dragleave', (event) => {
        if (!(event.relatedTarget instanceof Node) || !zone.contains(event.relatedTarget)) zone.removeAttribute(DROP);
      });
      zone.addEventListener('drop', (event) => {
        zone.removeAttribute(DROP);
        if (input.disabled) return;
        event.preventDefault();
        takeAll([...(event.dataTransfer?.files ?? [])]);
      });
    }

    for (const file of options.restored?.get(name) ?? []) {
      const built = rowOf(file.name);
      listOf().append(built.row);
      hold(built, file);
      built.drop.onclick = (): void => {
        removeRow(built.row);
        zone.dispatchEvent(new Event('input', { bubbles: true }));
        input.focus();
      };
    }
    zone.setAttribute('data-jfb-enhanced', '');
  }

  return {
    async settled(): Promise<boolean> {
      while (running.size > 0) await Promise.all(running);
      const failed = form.querySelector<HTMLElement>(`.jfb-file[${ERROR}]`);
      if (failed === null) return true;
      const retry = failed.querySelector<HTMLElement>('[data-jfb-retry]:not([hidden])');
      (retry ?? failed.querySelector<HTMLElement>('[data-jfb-drop-file]'))?.focus();
      say(
        `${failed.querySelector('strong')?.textContent ?? ''}: ${failed.querySelector('[class*="error"]')?.textContent ?? ''}`,
      );
      return false;
    },
    uploaded(): ReadonlyMap<string, readonly UploadedFile[]> {
      return new Map([...held].map(([name, files]) => [name, [...files.values()]]));
    },
  };
}
