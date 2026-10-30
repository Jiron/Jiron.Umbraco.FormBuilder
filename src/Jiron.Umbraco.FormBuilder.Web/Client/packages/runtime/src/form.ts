/**
 * The chunk the frame loads on a form's first interaction (ROADMAP 6.4b, ADR-0110): the shared logic and what the
 * runtime does with it. 6.4b reads the page; 6.4c applies the rules and 6.4d the checks to what it reads.
 */
import type { FormModel } from './model';
import { readPage, type PageReading } from './page';

/** An enhanced form with its logic. */
export interface FormSession {
  readonly form: HTMLFormElement;
  readonly model: FormModel;
  /** The form's answers as its controls hold them now. */
  read(): PageReading;
}

/** Gives a form its logic; the frame calls it once per form. */
export function attach(form: HTMLFormElement, model: FormModel): FormSession {
  return { form, model, read: () => readPage(form, model) };
}
