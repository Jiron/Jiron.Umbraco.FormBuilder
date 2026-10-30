/**
 * The form engine as the page runs it (ROADMAP 6.4f, ADR-0114): the model Core makes of the definition, here made of
 * the form endpoint's answer (`logicFormOf`, ADR-0111), with the calculation language when the form has a
 * calculation. The language is a chunk of its own, imported once for the page, so a form without a calculation never
 * loads it.
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

/** A form's engine. */
export interface PageEngine {
  /** The form as its rules see it. */
  readonly logic: LogicForm;
  /** Whether the form has a calculation. */
  readonly calculates: boolean;
  /** What the rules decide, and every calculation's result, for the answers in a context. */
  decide(answers: FormAnswers, context: Context): FormState;
}

/** A form's engine with the calculation language, which a form without a calculation does without (null). */
export function engineOf(model: FormModel, formulas: Formulas | null): PageEngine {
  return engine(logicFormOf(model.form, model.fields), formulas);
}

let language: Promise<Formulas> | undefined;

/** The calculation language, its own chunk, imported once for the page. */
export function loadFormulas(): Promise<Formulas> {
  return (language ??= import('@jfb/logic/calc').then((module) => module.FORMULAS));
}

/** A form's engine, the calculation language loaded first when the form has a calculation; rejected when it cannot. */
export async function loadEngine(model: FormModel, load: () => Promise<Formulas> = loadFormulas): Promise<PageEngine> {
  const logic = logicFormOf(model.form, model.fields);
  return engine(logic, hasCalculations(logic) ? await load() : null);
}

function engine(logic: LogicForm, formulas: Formulas | null): PageEngine {
  return {
    logic,
    calculates: hasCalculations(logic),
    decide: (answers, context) => evaluateForm(logic, answers, context, formulas),
  };
}
