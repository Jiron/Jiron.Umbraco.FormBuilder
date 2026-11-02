/**
 * Rules on the page (ROADMAP 6.4c, ADR-0111): the form engine of `packages/logic`, over the model Core makes of the
 * definition (`logicFormOf`), the answers the page holds (6.4b) and the context the server gives a render, decides what
 * is shown and required, and the page shows it as the server renders it (ADR-0015): an element a rule hides carries
 * `hidden` and `data-jfb-hidden` and its fields' controls are disabled, so they are never posted; the controls the
 * views mark `data-jfb-requires` take `required` while their field is required; a label's mark shows in the state its
 * `data-jfb-mark` names; and the note that explains the asterisk shows while one does. The same state gives each
 * calculation's result (6.4f, ADR-0114), and the same reading and state the visitor texts that recall answers (6.4g,
 * ADR-0115), shown for a form with either and no rule too.
 */
import { holdQuery, LogicError, type Context, type FormState, type LogicField, type LogicForm } from '@jfb/logic';
import type { PageEngine } from './engine';
import type { FormModel } from './model';
import { showCalculations } from './outputs';
import { readPage, rowOf, type PageReading } from './page';
import { showTexts } from './texts';

/** The rules, calculations and texts that recall answers of an enhanced form. */
export interface PageRules {
  /** What the rules decided last. */
  readonly state: FormState;
  /** Reads the page and shows the state its rules give; the state stays as it was when they cannot be worked out. */
  apply(): FormState;
}

const CONTROLS = 'input, select, textarea, button';

/**
 * The context of a render as far as a page knows it, as the server's render gives the rules one: the site's clock to
 * the second and its time zone, the page's culture, the member the challenge told of (a cached page is the same for
 * every visitor, so there is none before it, ADR-0156), and the query values of the names the form lists: those the page carries on after a post, or else its address's, as the
 * pipeline reads a first post's.
 */
const members = new WeakMap<FormModel, Context['member'] | undefined>();

/** Keeps what the challenge told of the member for the form's rules, which it asks only about what they look at. */
export const tellMember = (model: FormModel, member: Context['member'] | undefined): void => {
  members.set(model, member);
};

export function contextOf(model: FormModel, reading: PageReading, now: number, address: string): Context {
  const listed = model.form.settings?.queryValues ?? [];
  let query: ReadonlyMap<string, readonly string[]> = reading.post.query;
  if (query.size === 0) {
    const given = new Map<string, string[]>();
    for (const [name, value] of new URL(address).searchParams) {
      const values = given.get(name);
      if (values === undefined) given.set(name, [value]);
      else values.push(value);
    }
    query = given;
  }
  return {
    now: new Date(Math.floor(now / 1000) * 1000).toISOString().slice(0, 19) + 'Z',
    timeZone: model.timeZone,
    culture: model.culture,
    member: members.get(model) ?? { signedIn: false, groups: [] },
    query: holdQuery(query, listed),
    hidden: [],
  };
}

/** Whether a page, a group or a field has a rule: only then do the views write the hooks the rules are shown by. */
export function hasRules(form: LogicForm): boolean {
  const any = (rules: readonly unknown[] | undefined): boolean => (rules?.length ?? 0) > 0;
  const field = (one: LogicField): boolean => any(one.rules) || (one.fields ?? []).some(field);
  return form.pages.some(
    (page) =>
      any(page.rules) || (page.groups ?? []).some((group) => any(group.rules) || (group.fields ?? []).some(field)),
  );
}

/**
 * The rules, calculations and texts that recall answers of a form, shown once now and again after each answer the
 * visitor changes; null for a form with none of them, which the server renders as they leave it, or one whose rules
 * the engine refuses, which keeps the post's. A form without rules gets none of their hooks, only its calculations'
 * results and its texts.
 */
export function rulesOf(
  form: HTMLFormElement,
  model: FormModel,
  engine: PageEngine,
  clock: () => number = Date.now,
): PageRules | null {
  const ruled = hasRules(engine.logic);
  if (!ruled && !engine.calculates && engine.texts === null) return null;
  // The page that shows, which moves in the browser too (ADR-0155); one a rule just hid stays the page to come back to.
  let current = form.querySelector('[data-jfb-page]:not([hidden])');
  const pageNow = (): Element | null => (current = form.querySelector('[data-jfb-page]:not([hidden])') ?? current);
  const decide = (): { reading: PageReading; state: FormState } => {
    const reading = readPage(form, model);
    return {
      reading,
      state: engine.decide(reading.answers, contextOf(model, reading, clock(), form.ownerDocument.URL)),
    };
  };
  // A field a rule shows again holds its answer once its controls are enabled, and an element that depends on that
  // answer is decided again with it, until nothing more is shown: at most once per element, as rules have no cycle.
  const rounds = form.querySelectorAll('[data-jfb-page], [data-jfb-group], [data-jfb-field]').length + 1;
  const settle = (): FormState => {
    let decided = decide();
    for (let round = 1; ruled && show(form, decided.state, pageNow()) && round < rounds; round++) decided = decide();
    showCalculations(form, model, decided.state);
    if (engine.texts !== null) showTexts(form, model, engine.texts(decided.reading, decided.state));
    return decided.state;
  };
  let state: FormState;
  try {
    state = settle();
  } catch (error) {
    if (error instanceof LogicError) return null;
    throw error;
  }
  const rules = {
    state,
    apply(): FormState {
      try {
        rules.state = settle();
      } catch (error) {
        if (!(error instanceof LogicError)) throw error;
      }
      return rules.state;
    },
  };
  let queued = false;
  const later = (): void => {
    if (queued) return;
    queued = true;
    queueMicrotask(() => {
      queued = false;
      rules.apply();
    });
  };
  form.addEventListener('input', later);
  form.addEventListener('change', later);
  return rules;
}

/** Shows a state on the page; whether a field it hid is shown again, so its answer counts from the next reading. */
function show(form: HTMLFormElement, state: FormState, current: Element | null): boolean {
  const hidden = new Set(state.hidden);
  const required = new Set(state.required);
  for (const page of form.querySelectorAll<HTMLElement>('[data-jfb-page]')) {
    const ruled = hidden.has(page.dataset['jfbPage'] ?? '');
    flag(page, 'hidden', page !== current || ruled);
    flag(page, 'data-jfb-hidden', ruled);
  }
  for (const group of form.querySelectorAll<HTMLElement>('[data-jfb-group]')) {
    const ruled = hidden.has(group.dataset['jfbGroup'] ?? '');
    hide(group, ruled);
  }
  let shown = false;
  for (const field of form.querySelectorAll<HTMLElement>('[data-jfb-field]')) {
    const key = field.dataset['jfbField'] ?? '';
    const row = rowOf(field, state);
    let isHidden = hidden.has(key);
    let isRequired: boolean | null = required.has(key);
    if (row !== null) {
      // A repeatable group's child field takes its row's state; a row the reading could not count, the group's.
      isHidden = row.state === undefined ? hidden.has(row.group) : row.state.hidden.includes(key);
      isRequired = row.state === undefined ? null : row.state.required.includes(key);
    }
    if (field.hasAttribute('hidden') && !isHidden) shown = true;
    hide(field, isHidden);
    for (const control of own(field, CONTROLS))
      flag(control, 'disabled', isHidden || control.hasAttribute('data-jfb-disabled'));
    if (isRequired === null) continue;
    for (const control of own(field, '[data-jfb-requires]')) flag(control, 'required', isRequired);
    for (const mark of own(field, '[data-jfb-mark]'))
      flag(mark, 'hidden', (mark.dataset['jfbMark'] === 'required') !== isRequired);
  }
  const note = form.querySelector('[data-jfb-required-note]');
  if (note !== null) {
    const asterisk = [...form.querySelectorAll<HTMLElement>('[data-jfb-mark]')].some(
      (mark) => !mark.hasAttribute('hidden') && mark.closest('[data-jfb-hidden]') === null,
    );
    flag(note, 'hidden', !asterisk);
  }
  return shown;
}

/** The elements of a field's own, not those of a repeatable group's child fields inside it. */
const own = (field: HTMLElement, selector: string): HTMLElement[] =>
  [...field.querySelectorAll<HTMLElement>(selector)].filter((each) => each.closest('[data-jfb-field]') === field);

/** Sets or removes an attribute only when it changes, so the page's own observers see only real changes. */
function flag(element: Element, name: string, on: boolean): void {
  if (element.hasAttribute(name) !== on) element.toggleAttribute(name, on);
}

/** Hides or shows an element as a rule decides. */
function hide(element: Element, on: boolean): void {
  flag(element, 'hidden', on);
  flag(element, 'data-jfb-hidden', on);
}
