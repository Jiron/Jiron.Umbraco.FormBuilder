/**
 * Validation on the page (ROADMAP 6.4d, ADR-0112): the page's answers read (6.4b), its rules decided (6.4c), its
 * shown fields checked as the pipeline checks a post, and each field's first error worded as the server's render
 * words it. A send checks every page and refuses an empty submission; Next checks the page shown; the fields' own
 * check, as the visitor answers, leaves the empty submission to the send (6.4e). A label that recalls answers is
 * named as it renders for the answers checked (6.4g, ADR-0115).
 */
import { isEmptyPost } from '@jfb/logic';
import { errorsOf } from './checks';
import type { PageEngine } from './engine';
import { emptyError, shownErrors, wordsFor, type ShownError } from './messages';
import type { FormModel } from './model';
import { readPage } from './page';
import { contextOf } from './rules';

/** What a check covers: a send, every page and the empty submission; or the page shown, as Next asks. */
export type CheckScope = 'send' | 'page';

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

async function check(
  form: HTMLFormElement,
  model: FormModel,
  engine: PageEngine,
  scope: CheckScope | 'fields',
  clock: () => number,
): Promise<ShownError[]> {
  const reading = readPage(form, model);
  const context = contextOf(model, reading, clock(), form.ownerDocument.URL);
  const state = engine.decide(reading.answers, context);
  const words = await wordsFor(model.culture);
  if (scope === 'send' && isEmptyPost(reading.post)) return [emptyError(form, model, words)];
  let errors = errorsOf(model, reading, state, context);
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
