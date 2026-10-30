/**
 * Validation on the page (ROADMAP 6.4d, ADR-0112): the page's answers read (6.4b), its rules decided (6.4c), its
 * shown fields checked as the pipeline checks a post, and each field's first error worded as the server's render
 * words it. A send checks every page and refuses an empty submission; Next checks the page shown; the fields' own
 * check, as the visitor answers, leaves the empty submission to the send (6.4e). A label that recalls answers is
 * named as it renders for the answers checked (6.4g, ADR-0115). The server's errors of a refused send are worded and
 * placed as the page's own (6.4i, ADR-0120).
 */
import { isEmptyPost } from '@jfb/logic';
import { errorsOf, type PageError } from './checks';
import type { PageEngine } from './engine';
import { emptyError, shownErrors, wordsFor, type ShownError } from './messages';
import type { FormModel } from './model';
import { fieldsOf, readPage } from './page';
import { contextOf } from './rules';

/** What a check covers: a send, every page and the empty submission; or the page shown, as Next asks. */
export type CheckScope = 'send' | 'page';

/** An error of the entries endpoint's `422`: the field's kind of error, the field by its path, its check or rule. */
export interface ServerProblem {
  readonly code: string;
  readonly path: string;
  readonly check?: number | null;
  readonly rule?: string | null;
}

/** The errors the page shows for its answers now, in the order it shows them. */
export function checkPage(
  form: HTMLFormElement,
  model: FormModel,
  engine: PageEngine,
  scope: CheckScope,
  clock: () => number = Date.now,
): Promise<ShownError[]> {
  return check(form, model, engine, scope, clock);
}

/** Every shown field's error for the answers now, without the empty submission's, which only a send has. */
export function checkFields(
  form: HTMLFormElement,
  model: FormModel,
  engine: PageEngine,
  clock: () => number = Date.now,
): Promise<ShownError[]> {
  return check(form, model, engine, 'fields', clock);
}

/**
 * The server's errors of a refused send as the page shows them, each at the field its path names, as the post without
 * JavaScript shows them; an error at no field of the form is left out, as there.
 */
export function serverErrors(
  form: HTMLFormElement,
  model: FormModel,
  engine: PageEngine,
  problems: readonly ServerProblem[],
  clock: () => number = Date.now,
): Promise<ShownError[]> {
  return check(form, model, engine, problems, clock);
}

/**
 * Where a server error's path is (ADR-0088): `alias`, `alias.<row key>` for a grid's row, `group[row].child` and
 * `group[row].child.<row key>`, as Web's ProblemPaths finds it; null for a path of no field.
 */
export function placeOf(model: FormModel, problem: ServerProblem): PageError | null {
  const found = /^([^.[]+)(?:\[(\d+)\]\.([^.]+))?(?:\.(.+))?$/.exec(problem.path);
  const top = fieldsOf(model).find((field) => field.alias === found?.[1]);
  const field = found?.[3] === undefined ? top : top?.fields?.find((child) => child.alias === found[3]);
  if (found === null || field === undefined) return null;
  return {
    field: field.key,
    row: found[2] === undefined ? null : Number(found[2]),
    code: problem.code,
    ...(problem.check == null ? {} : { check: problem.check }),
    ...(problem.rule == null ? {} : { rule: problem.rule }),
    ...(found[4] === undefined ? {} : { gridRow: found[4] }),
  };
}

async function check(
  form: HTMLFormElement,
  model: FormModel,
  engine: PageEngine,
  scope: CheckScope | 'fields' | readonly ServerProblem[],
  clock: () => number,
): Promise<ShownError[]> {
  const reading = readPage(form, model);
  const context = contextOf(model, reading, clock(), form.ownerDocument.URL);
  const state = engine.decide(reading.answers, context);
  const words = await wordsFor(model.culture);
  const server = typeof scope === 'object';
  if (server ? scope.some((problem) => problem.code === 'empty') : scope === 'send' && isEmptyPost(reading.post))
    return [emptyError(form, model, words)];
  let errors = server
    ? scope.map((problem) => placeOf(model, problem)).filter((error) => error !== null)
    : errorsOf(model, reading, state, context);
  if (scope === 'page') {
    const key = form.querySelector<HTMLElement>('[data-jfb-page]:not([hidden])')?.dataset['jfbPage'];
    const page = model.form.pages.find((each) => each.key === key);
    const onPage = new Set(
      (page?.groups ?? []).flatMap((group) =>
        group.fields.flatMap((field) => [field.key, ...(field.fields ?? []).map((child) => child.key)]),
      ),
    );
    errors = errors.filter((error) => onPage.has(error.field));
  }
  const texts = engine.texts === null ? undefined : engine.texts(reading, state);
  return shownErrors(form, model, reading, errors, context, words, texts);
}
