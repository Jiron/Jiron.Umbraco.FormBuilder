/** What a form's send knows of its uploads (ROADMAP 6.4m, ADR-0152), whose work is the chunks `uploads-chunk` and `signature-chunk`. */
import type { FormModel } from './model';
import { fieldsOf } from './page';

/** A file the server holds for a field. */
export interface UploadedFile {
  readonly token: string;
  readonly name: string;
  readonly size: number;
  /** A drawn signature's strokes, which the form in this one's place shows again (6.4j). */
  readonly strokes?: readonly (readonly number[])[];
}

/** What a field's hidden input holds while its token is on its way, so a check counts the field as answered; a send waits for the token. */
export const ON_ITS_WAY = 'on-its-way';

/** What a form's uploads offer its send. */
export interface PageUploads {
  /**
   * Settles once no upload is on its way: true for a form that may go, false for one with a file that failed, which has the
   * focus. A signature that cannot be sent rejects with the status of the answer.
   */
  settled(): Promise<boolean>;
  /** The files the server holds, by the name their field posts them under. */
  uploaded(): ReadonlyMap<string, readonly UploadedFile[]>;
}

export interface UploadOptions {
  /** The public API's base. */
  readonly api: string;
  /** The challenge an upload's first part holds; null when none came. */
  challenge(): Promise<string | null>;
  /** A new challenge for one that has expired. */
  renew(): Promise<string | null>;
  /** The files of the form this one came in place of (6.4j). */
  readonly restored?: ReadonlyMap<string, readonly UploadedFile[]>;
}

/**
 * Whether a form takes its files on the page: one it can send from there, whose every field that stores a file has what the
 * page needs in its view, and no button that posts on the way, as a page's Next, a row's Add and a ranking's moves do until 6.5d
 * and 6.4o: a post cannot claim a token, and would lose a drawing.
 */
export function takesUploads(form: HTMLFormElement, model: FormModel): boolean {
  if (form.querySelectorAll('[data-jfb-page]').length > 1 || form.querySelector('.jfb-repeater, .jfb-ranking') !== null)
    return false;
  if (form.querySelector(':scope > template[data-jfb-templates]')?.hasAttribute('data-jfb-sending') !== true)
    return false;
  const files = fieldsOf(model)
    .flatMap((field) => [field, ...(field.fields ?? [])])
    .filter((field) => model.fields[field.key]?.kind === 'files');
  return (
    files.length > 0 &&
    files.every((field) =>
      [...form.querySelectorAll(`[data-jfb-field="${field.key}"]`)].every(
        (each) => each.querySelector('template[data-jfb-file], canvas.jfb-signature-pad') !== null,
      ),
    ) &&
    files.every((field) => field.type === 'file' || field.type === 'signature')
  );
}

/** A form's uploads of each kind, as one. */
export function allOf(parts: readonly PageUploads[]): PageUploads {
  return {
    settled: async () => (await Promise.all(parts.map((part) => part.settled()))).every(Boolean),
    uploaded: () => new Map(parts.flatMap((part) => [...part.uploaded()])),
  };
}
