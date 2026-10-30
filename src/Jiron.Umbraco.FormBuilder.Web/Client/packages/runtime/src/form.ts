/**
 * The chunk the frame loads on a form's first interaction (ROADMAP 6.4b, ADR-0110): the shared logic and what the
 * runtime does with it. 6.4b reads the page, 6.4c applies the rules to what it reads (ADR-0111), 6.4d checks the
 * answers (ADR-0112), 6.4e shows their errors (ADR-0113), 6.4f works the calculations out, with the calculation
 * language loaded first as a chunk of its own for a form that has one, and keeps the sliders' values (ADR-0114), and
 * 6.4g renders the visitor texts that recall answers as the visitor types, with the merge tags loaded first as a
 * chunk of their own for a form that has such a text (ADR-0115).
 */
import { loadEngine, type PageEngine } from './engine';
import { pageErrorsOf } from './errors';
import type { FormModel } from './model';
import { followSliders } from './outputs';
import { readPage, type PageReading } from './page';
import type { ShownError } from './messages';
import { rulesOf, type PageRules } from './rules';
import { checkPage, type CheckScope } from './validation';

/** An enhanced form with its logic. */
export interface FormSession {
  readonly form: HTMLFormElement;
  readonly model: FormModel;
  /** The form engine it decides by, with the calculation language when the form has a calculation. */
  readonly engine: PageEngine;
  /** The form's answers as its controls hold them now. */
  read(): PageReading;
  /** The form's rules and calculations, shown on the page as the visitor answers; null for a form with neither. */
  readonly rules: PageRules | null;
  /** The errors the page shows for its answers now (ADR-0112): a send's, or the page's that Next checks. */
  check(scope: CheckScope): Promise<ShownError[]>;
  /** Checks as a send or Next does and shows what stops it on the page (ADR-0113); true when nothing does. */
  validate(scope: CheckScope): Promise<boolean>;
}

/**
 * Gives a form its logic; the frame calls it once per form. A form whose calculation language cannot load is left to
 * the post without JavaScript (null), as one whose chunk cannot.
 */
export async function attach(form: HTMLFormElement, model: FormModel): Promise<FormSession | null> {
  const engine = await loadEngine(model).catch(() => null);
  if (engine === null) return null;
  const rules = rulesOf(form, model, engine);
  followSliders(form, model);
  const errors = pageErrorsOf(form, model, engine);
  return {
    form,
    model,
    engine,
    read: () => readPage(form, model),
    rules,
    check: (scope) => checkPage(form, model, engine, scope),
    validate: (scope) => errors.validate(scope),
  };
}
