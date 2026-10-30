/**
 * The chunk the frame loads on a form's first interaction (ROADMAP 6.4b, ADR-0110): the shared logic and what the
 * runtime does with it. 6.4b reads the page, 6.4c applies the rules to what it reads (ADR-0111), and 6.4d and 6.4e
 * the checks.
 */
import type { FormModel } from './model';
import { readPage, type PageReading } from './page';
import type { ShownError } from './messages';
import { rulesOf, type PageRules } from './rules';
import { checkPage, type CheckScope } from './validation';

/** An enhanced form with its logic. */
export interface FormSession {
  readonly form: HTMLFormElement;
  readonly model: FormModel;
  /** The form's answers as its controls hold them now. */
  read(): PageReading;
  /** The form's rules, shown on the page as the visitor answers; null for a form without rules. */
  readonly rules: PageRules | null;
  /** The errors the page shows for its answers now (ADR-0112): a send's, or the page's that Next checks. */
  check(scope: CheckScope): Promise<ShownError[]>;
}

/** Gives a form its logic; the frame calls it once per form. */
export function attach(form: HTMLFormElement, model: FormModel): FormSession {
  return {
    form,
    model,
    read: () => readPage(form, model),
    rules: rulesOf(form, model),
    check: (scope) => checkPage(form, model, scope),
  };
}
