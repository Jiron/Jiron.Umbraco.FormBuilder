/**
 * The default values that hold merge tags, filled in as the form is shown (ROADMAP 6.4h, ADR-0118), a chunk of its own
 * with the merge tags. The server leaves such a default empty, so a cached page is the same for every visitor, and
 * marks each field whose controls start from one `data-jfb-default`. The page renders it with the query values of its
 * address that the form allows and the form endpoint's values for its other tags, writes a tag its place refuses as it
 * stands, and gives the result to the controls the visitor has not changed yet, before the rules first decide. A
 * default the endpoint does not list renders as it is written. A field prefilled from the query takes the address's
 * value, where it has one, before its default (ADR-0124). Only the control named as the field takes it, as on the
 * server: a choice's "Other" text, a name's or an address's parts and an email's repeat stay empty (6.8b).
 */
import type { Context } from '@jfb/logic';
import { parseTemplate, renderTemplate, type MergeValue } from '@jfb/logic/template';
import type { FormModel } from './model';
import { allFieldsOf } from './page';

type Control = HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;

const own = <T>(record: Readonly<Record<string, T>>, key: string): T | undefined =>
  Object.hasOwn(record, key) ? record[key] : undefined;

/** Fills in the defaults of the fields the server marked, once: the marks go as they are filled. */
export function fillDefaults(form: HTMLFormElement, model: FormModel, context: Context): void {
  const tags = model.mergeTags;
  const listed = new Map(
    (tags?.texts ?? []).filter((text) => text.text === 'defaultValue').map((text) => [text.owner, text.refused ?? []]),
  );
  const given = own(tags?.values ?? {}, 'text') ?? {};
  // A query value as the server's source gives one: one text, a list of several, or the empty text for none.
  const values = (path: string): MergeValue | undefined => {
    if (!path.startsWith('query.')) return own(given, path);
    const found = own(context.query, path.slice(6)) ?? [];
    return found.length === 1 ? found[0] : found.length === 0 ? '' : found;
  };
  const all = allFieldsOf(model);
  for (const element of form.querySelectorAll<HTMLElement>('[data-jfb-default]')) {
    element.removeAttribute('data-jfb-default');
    const field = all.find((each) => each.key === element.dataset['jfbField']);
    const written = field?.defaultValue;
    const refused = field === undefined ? undefined : listed.get(field.key);
    const texts = (typeof written === 'string' ? [written] : (written ?? []))
      .map((template) =>
        refused === undefined
          ? template
          : renderTemplate(
              parseTemplate(template, 'text', (path) => (refused.includes(path) ? 'source_not_for_place' : null)),
              values,
              model.formats,
            ).text,
      )
      .filter((text) => text.length > 0);
    // A field prefilled from the query takes the address's value first (ADR-0124).
    const asked = field?.prefill?.query;
    const prefilled =
      typeof asked === 'string' ? [own(context.query, asked) ?? []].flat().filter((text) => text.length > 0) : [];
    fill(element, field?.alias, prefilled.length > 0 ? prefilled : texts);
  }
}

/**
 * Gives the field's own controls, named as the field (a row's by its row's path), its default's texts, unless it has
 * none or the visitor changed one of the field's controls already.
 */
function fill(element: HTMLElement, alias: string | null | undefined, texts: readonly string[]): void {
  const controls = [...element.querySelectorAll<Control>('input, select, textarea')].filter(
    (control) => control.type !== 'hidden',
  );
  if (texts.length === 0 || controls.some(changed)) return;
  for (const control of controls.filter((each) => each.name.replace(/^.*\]\./, '') === alias)) {
    if (control instanceof HTMLSelectElement) {
      const options = [...control.options];
      if (!options.some((option) => texts.includes(option.value))) continue;
      if (control.multiple) for (const option of options) option.selected = texts.includes(option.value);
      else control.value = texts.find((text) => options.some((option) => option.value === text)) ?? control.value;
    } else if (checkable(control)) control.checked = texts.includes(control.value);
    else control.value = texts[0] ?? '';
  }
}

/** Whether a control is a box or a radio button, which a default ticks. */
const checkable = (control: Control): control is HTMLInputElement =>
  control instanceof HTMLInputElement && (control.type === 'checkbox' || control.type === 'radio');

/** Whether the visitor changed a control from what the server wrote. */
function changed(control: Control): boolean {
  if (control instanceof HTMLSelectElement) {
    const options = [...control.options];
    return control.multiple
      ? options.some((option) => option.selected !== option.defaultSelected)
      : control.value !== (options.find((option) => option.defaultSelected) ?? options[0])?.value;
  }
  return checkable(control) ? control.checked !== control.defaultChecked : control.value !== control.defaultValue;
}
