/** What a form's send knows of its uploads (ROADMAP 6.4m, ADR-0152), whose work is the chunk `uploads-chunk`. */
import type { FormModel } from './model';
import { fieldsOf } from './page';

/** A file the server holds for a field. */
export interface UploadedFile {
  readonly token: string;
  readonly name: string;
  readonly size: number;
}

/** What a form's uploads offer its send. */
export interface PageUploads {
  /** Settles once no upload is on its way. */
  settled(): Promise<void>;
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

/** Whether a form takes its files on the page: one page, whose every file field is one the page can read. */
export function takesUploads(form: HTMLFormElement, model: FormModel): boolean {
  if (form.querySelectorAll('[data-jfb-page]').length > 1) return false;
  const files = fieldsOf(model)
    .flatMap((field) => [field, ...(field.fields ?? [])])
    .filter((field) => model.fields[field.key]?.kind === 'files');
  return files.length > 0 && files.every((field) => field.type === 'file');
}
