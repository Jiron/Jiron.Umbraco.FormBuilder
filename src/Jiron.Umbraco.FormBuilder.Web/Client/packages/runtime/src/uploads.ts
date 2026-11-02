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
  settled(within?: Element): Promise<boolean>;
  /** The files the server holds, by the name their field posts them under. */
  uploaded(): ReadonlyMap<string, readonly UploadedFile[]>;
  /** Takes on the fields of a row added in place (ADR-0154). */
  add?(root: Element): void;
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

/** Whether a form of several pages can move through them on the page: its view writes every button for that (ADR-0155). */
export const movesPages = (form: HTMLFormElement): boolean =>
  form
    .querySelector<HTMLTemplateElement>(':scope > template[data-jfb-templates]')
    ?.content.querySelector('[data-jfb-nav="next"]') != null;

/** Whether a ranking or a grid takes its options from a parent, which only a post writes anew (ADR-0154). */
export const optionsByPost = (model: FormModel): boolean =>
  fieldsOf(model)
    .flatMap((field) => [field, ...(field.fields ?? [])])
    .some((field) => (field.type === 'ranking' || field.type === 'grid') && field.options?.parent != null);

/**
 * Whether a form takes its files on the page: one it can send from there, whose every field that stores a file has what the
 * page needs in its view, and no button that posts on the way, as the step onto a summary page does, and a page's Next in a view
 * that writes no buttons for the page to move with: a post cannot claim a token, and would lose a drawing.
 */
export function takesUploads(form: HTMLFormElement, model: FormModel): boolean {
  const templates = form.querySelector<HTMLTemplateElement>(':scope > template[data-jfb-templates]');
  if (optionsByPost(model) || templates?.hasAttribute('data-jfb-sending') !== true) return false;
  if (
    form.querySelectorAll('[data-jfb-page]').length > 1 &&
    ((model.form.settings?.summaryPage === true && form.hasAttribute('action')) || !movesPages(form))
  )
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
    settled: async (within) => (await Promise.all(parts.map((part) => part.settled(within)))).every(Boolean),
    uploaded: () => new Map(parts.flatMap((part) => [...part.uploaded()])),
    add: (root) => {
      for (const part of parts) part.add?.(root);
    },
  };
}
