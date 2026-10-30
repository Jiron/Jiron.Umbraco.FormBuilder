/**
 * The form engine as the page runs it (ROADMAP 6.4f, ADR-0114): the model Core makes of the definition, here made of
 * the form endpoint's answer (`logicFormOf`, ADR-0111), with the calculation language when the form has a
 * calculation, and since 6.4g the renderer of the visitor texts that recall answers when it has one (ADR-0115). Each
 * is a chunk of its own, imported once for the page, so a form without a calculation or such a text never loads it.
 */
import {
  evaluateForm,
  hasCalculations,
  logicFormOf,
  type Context,
  type FormAnswers,
  type Formulas,
  type FormState,
  type LogicForm,
} from '@jfb/logic';
import type { FormModel } from './model';
import type { PageReading } from './page';

/**
 * The visitor texts that recall answers, rendered for a reading and the state its rules decided, by `<owner> <name>`
 * as the views mark them (`data-jfb-text`).
 */
export type TextRenderer = (reading: PageReading, state: FormState) => ReadonlyMap<string, string>;

/** A form's engine. */
export interface PageEngine {
  /** The form as its rules see it. */
  readonly logic: LogicForm;
  /** Whether the form has a calculation. */
  readonly calculates: boolean;
  /** What the rules decide, and every calculation's result, for the answers in a context. */
  decide(answers: FormAnswers, context: Context): FormState;
  /** The renderer of the texts that recall answers; null for a form without one. */
  readonly texts: TextRenderer | null;
}

/**
 * A form's engine with the calculation language and the texts' renderer, which a form without a calculation or a
 * text that recalls answers does without (null).
 */
export function engineOf(model: FormModel, formulas: Formulas | null, texts: TextRenderer | null = null): PageEngine {
  return engine(logicFormOf(model.form, model.fields), formulas, texts);
}

let language: Promise<Formulas> | undefined;

/** The calculation language, its own chunk, imported once for the page. */
export function loadFormulas(): Promise<Formulas> {
  return (language ??= import('@jfb/logic/calc').then((module) => module.FORMULAS));
}

/** What makes a form's texts' renderer, from the chunk of its own. */
export type TextsOf = (model: FormModel) => TextRenderer;

let renderer: Promise<TextsOf> | undefined;

/** The renderer of the texts that recall answers, with the merge tags, its own chunk, imported once for the page. */
export function loadTexts(): Promise<TextsOf> {
  return (renderer ??= import('./recall').then((module) => module.textsOf));
}

/** A field of a recall form, as far as it says whether a text recalls it. */
interface Named {
  readonly alias?: string | null;
  readonly fields?: readonly Named[];
}

/**
 * Whether a form has a visitor text that recalls answers: its recall form names a field, as it does only for a field
 * a text recalls (ADR-0085), so its texts can change as the visitor answers.
 */
export function recallsAnswers(model: FormModel): boolean {
  const names = (field: Named): boolean => typeof field.alias === 'string' || (field.fields ?? []).some(names);
  return (model.mergeTags?.recall.pages ?? []).some((page) =>
    (page.groups ?? []).some((group) => (group.fields ?? []).some(names)),
  );
}

/**
 * A form's engine, the calculation language and the texts' renderer loaded first when the form needs them; rejected
 * when one cannot load.
 */
export async function loadEngine(
  model: FormModel,
  load: () => Promise<Formulas> = loadFormulas,
  texts: () => Promise<TextsOf> = loadTexts,
): Promise<PageEngine> {
  const logic = logicFormOf(model.form, model.fields);
  const [formulas, textsOf] = await Promise.all([
    hasCalculations(logic) ? load() : null,
    recallsAnswers(model) ? texts() : null,
  ]);
  return engine(logic, formulas, textsOf === null ? null : textsOf(model));
}

function engine(logic: LogicForm, formulas: Formulas | null, texts: TextRenderer | null): PageEngine {
  return {
    logic,
    calculates: hasCalculations(logic),
    decide: (answers, context) => evaluateForm(logic, answers, context, formulas),
    texts,
  };
}
