/**
 * The page's messages (ROADMAP 6.4d, ADR-0112): each field's first error as the page shows it, as Web's render words it
 * (FormModels.Errors): the editor's own message as written, otherwise the default in the page's language with the
 * field's label, and in a row the row's legend, before it in the error summary. The words of each language are a file
 * of their own, loaded for the page's culture only. A label that recalls answers is named as it renders for the
 * answers checked (6.4g, ADR-0115).
 */
import {
  BUILT_IN_TYPES,
  defaultMessage,
  summaryEntry,
  today,
  type Context,
  type MessageWords,
  type VisitorError,
} from '@jfb/logic';
import type { PageError } from './checks';
import type { FormModel, ModelField } from './model';
import { allFieldsOf, builtIn, fieldsOf, type PageReading } from './page';

/** An error as the page shows it. */
export interface ShownError {
  /** The field's key, and the row of the repeatable group it is in, or null. */
  readonly field: string;
  readonly row: number | null;
  readonly code: string;
  /** The control's id, which the error's paragraph (`<target>-error`) and the summary's link name. */
  readonly target: string;
  /** The message under the field. */
  readonly message: string;
  /** The summary link's text. */
  readonly link: string;
  /** For a name or an address, the parts the page marks invalid. */
  readonly parts?: readonly string[];
}

const LANGUAGES = {
  en: () => import('@jfb/logic/lang/en').then((module) => module.en),
  de: () => import('@jfb/logic/lang/de').then((module) => module.de),
} as const satisfies Readonly<Record<string, () => Promise<MessageWords>>>;

/** A language whose words the runtime has. */
type Language = keyof typeof LANGUAGES;

const loaded = new Map<string, Promise<MessageWords>>();

/** The language whose words a culture reads: its own, its more general culture's, else English, as Core's. */
export function languageOf(culture: string): Language {
  let candidate = culture.toLowerCase();
  while (candidate.length > 0) {
    if (Object.hasOwn(LANGUAGES, candidate)) return candidate as Language;
    const dash = candidate.lastIndexOf('-');
    candidate = dash < 0 ? '' : candidate.slice(0, dash);
  }
  return 'en';
}

/** The words of a culture's language, loaded the first time they are needed. */
export function wordsFor(culture: string): Promise<MessageWords> {
  const language = languageOf(culture);
  let words = loaded.get(language);
  if (words === undefined) {
    words = LANGUAGES[language]();
    loaded.set(language, words);
  }
  return words;
}

/**
 * Each field's first error, in the order the page shows them: the page's fields in its order, a repeatable group's
 * rows' children before the group's own, as the error summary lists them. `texts` are the visitor texts that recall
 * answers, rendered for the same answers, by `<owner> <name>`.
 */
export function shownErrors(
  form: HTMLFormElement,
  model: FormModel,
  reading: PageReading,
  errors: readonly PageError[],
  context: Context,
  words: MessageWords,
  texts: ReadonlyMap<string, string> = NO_TEXTS,
): ShownError[] {
  const first = new Map<string, PageError>();
  for (const error of errors) {
    const at = `${error.field} ${String(error.row)}`;
    if (!first.has(at)) first.set(at, error);
  }
  const day = today(context.now, context.timeZone);
  const shown: ShownError[] = [];
  const add = (field: ModelField, row: number | null, group: ModelField | null): void => {
    const error = first.get(`${field.key} ${String(row)}`);
    if (error === undefined) return;
    const prefix = row === null || group === null ? form.id : `${form.id}-${group.alias ?? ''}-${String(row)}`;
    const target = `${prefix}-${field.alias ?? field.key.replaceAll('-', '')}`;
    const element = elementOf(form, field.key, row, group?.key ?? null);
    const parts = error.parts === undefined ? {} : { parts: error.parts };
    const own = ownMessage(model, field, error);
    if (own !== null) {
      shown.push({ field: field.key, row, code: error.code, target, message: own, link: own, ...parts });
      return;
    }
    const posted =
      row === null ? reading.fields.get(field.key) : reading.groups.get(group?.key ?? '')?.rows[row]?.get(field.key);
    const check = error.check === undefined ? undefined : field.validation?.[error.check];
    const other =
      check?.kind === 'matchesField' && check.field !== undefined ? labelOfKey(form, model, check.field, texts) : null;
    const visitorError: VisitorError = {
      code: error.code,
      field: {
        type: field.type,
        builtIn: builtIn(model, field),
        settings: field.settings ?? {},
        options: (field.options?.items ?? []).map((item) => ({ value: item.value, label: item.label ?? '' })),
      },
      ...(check === undefined || !CHECK_KINDS.has(check.kind) ? {} : { check: check.kind }),
      ...(other === null ? {} : { other }),
      gridRow: error.gridRow !== undefined,
      answers: posted?.posted?.values ?? [],
      ...(reading.groups.has(field.key) ? { rows: reading.groups.get(field.key)?.count ?? 0 } : {}),
    };
    const message = defaultMessage(visitorError, words, model.formats, day, model.words);
    const gridRow =
      error.gridRow === undefined ? null : textOf(field.rows?.find((each) => each.key === error.gridRow)?.label);
    const legend = gridRow ?? (row === null ? null : legendOf(element));
    const label = field.hideLabel === true || !holdsValue(model, field) ? null : labelOf(element, field, texts);
    shown.push({
      field: field.key,
      row,
      code: error.code,
      target,
      message: gridRow === null ? message : summaryEntry(message, gridRow, null, words),
      link: summaryEntry(message, label, legend, words),
      ...parts,
    });
  };
  for (const field of fieldsOf(model)) {
    const group = reading.groups.get(field.key);
    if (group !== undefined)
      group.rows.forEach((_, row) => {
        for (const child of field.fields ?? []) add(child, row, field);
      });
    add(field, null, null);
  }
  return shown;
}

/**
 * The empty submission's message, linked as the server links it: to the first field of the page shown, in a group
 * the rules show, that the rules show and that takes an answer; to the form without one.
 */
export function emptyError(form: HTMLFormElement, model: FormModel, words: MessageWords): ShownError {
  const page = form.querySelector<HTMLElement>('[data-jfb-page]:not([hidden])');
  const shown = model.form.pages.find((each) => each.key === page?.dataset['jfbPage']);
  let target = form.id;
  for (const group of shown?.groups ?? []) {
    if (page?.querySelector(`[data-jfb-group="${group.key}"]`)?.hasAttribute('hidden') === true) continue;
    const field = group.fields.find((each) => {
      const element = elementOf(form, each.key, null, null);
      return element !== null && !element.hasAttribute('hidden') && holdsValue(model, each);
    });
    if (field !== undefined) {
      target = `${form.id}-${field.alias ?? field.key.replaceAll('-', '')}`;
      break;
    }
  }
  return { field: '', row: null, code: 'empty', target, message: words.empty, link: words.empty };
}

/** No visitor text that recalls answers. */
const NO_TEXTS: ReadonlyMap<string, string> = new Map();

/** The check kinds the default messages name; any other says "Check this answer". */
const CHECK_KINDS = new Set(['pattern', 'matchesField', 'uniquePerForm']);

/** The editor's own message for an error: the check's, the validation rule's or the field's for the kind; an empty one is none. */
function ownMessage(model: FormModel, field: ModelField, error: PageError): string | null {
  let own: string | null | undefined;
  if (error.code === 'check') own = error.check === undefined ? undefined : field.validation?.[error.check]?.message;
  else if (error.code === 'rule') own = model.form.validationRules?.find((rule) => rule.key === error.rule)?.message;
  else
    own = field.messages != null && Object.hasOwn(field.messages, error.code) ? field.messages[error.code] : undefined;
  return textOf(own);
}

const textOf = (text: string | null | undefined): string | null => (text == null || text.length === 0 ? null : text);

/** Whether a field takes an answer, so it has a label; a site's own type is taken to. */
function holdsValue(model: FormModel, field: ModelField): boolean {
  if (!builtIn(model, field)) return true;
  return !Object.hasOwn(BUILT_IN_TYPES, field.type) || BUILT_IN_TYPES[field.type]?.holdsValue !== false;
}

/** A field's element on the page: outside any row, or in a repeatable group's row. */
export function elementOf(
  form: HTMLFormElement,
  key: string,
  row: number | null,
  group: string | null,
): HTMLElement | null {
  if (row === null || group === null)
    return (
      [...form.querySelectorAll<HTMLElement>(`[data-jfb-field="${key}"]`)].find(
        (each) => each.parentElement?.closest('[data-jfb-row]') == null,
      ) ?? null
    );
  const rows = elementOf(form, group, null, null)?.querySelector(`[data-jfb-row="${String(row)}"]`);
  return rows?.querySelector<HTMLElement>(`[data-jfb-field="${key}"]`) ?? null;
}

/**
 * A field's label: as it renders for the answers when it recalls them, an empty one as none; otherwise as the page
 * shows it, without its mark; a box's field label, which the page may not show, from the model.
 */
function labelOf(element: HTMLElement | null, field: ModelField, texts: ReadonlyMap<string, string>): string | null {
  const recalled = texts.get(`${field.key} label`);
  if (recalled !== undefined) return textOf(recalled);
  const shown = element?.querySelector(':scope > label.jfb-label, :scope > fieldset > legend.jfb-label');
  if (shown == null) return textOf(field.label);
  return textOf(textWithout(shown, '.jfb-mark').trim());
}

/** The label of a field by its key, as `matchesField` names the other field. */
function labelOfKey(
  form: HTMLFormElement,
  model: FormModel,
  key: string,
  texts: ReadonlyMap<string, string>,
): string | null {
  const field = allFieldsOf(model).find((each) => each.key === key);
  if (field === undefined) return null;
  return labelOf(elementOf(form, key, null, null), field, texts);
}

/** The legend of the row a field is in. */
function legendOf(element: HTMLElement | null): string | null {
  const legend = element?.closest('[data-jfb-row]')?.querySelector(':scope > legend');
  return legend == null ? null : textOf(legend.textContent);
}

/** An element's text without that of the descendants a selector names. */
function textWithout(element: Element, selector: string): string {
  let text = '';
  for (const node of element.childNodes) {
    if (node.nodeType === Node.TEXT_NODE) text += node.textContent ?? '';
    else if (node instanceof Element && !node.matches(selector)) text += textWithout(node, selector);
  }
  return text;
}
