// ADR-0015's contract test on the element's output (ROADMAP 6.6e, as Rendering/FormRenderingKitchenTests holds the
// server's): the kitchen sink of every built-in type, written by the element in each built-in theme and with every field
// hidden by its rule, in English and in German, carries the markup contract's hooks: the form's attributes, its model
// data, templates, live region and honeypot; each field's wrapper, its control's id and names below the instance, its
// label or legend and its help; hidden fields hidden with their controls disabled; a row's fields with the row's ids and
// path; the navigation's buttons; and the theme's classes on each slot beside ours, and no class of anyone else's.
import { describe, expect, it } from 'vitest';
import type { FieldViewModel, FormViewModel } from './answer';
import { LANGUAGES, type ElementCase } from './testing';
import { writeForm } from './views';

// The types without an alias: the content types and the captcha's place (ADR-0015).
const UNNAMED = new Set(['heading', 'paragraph', 'divider', 'image', 'captcha']);

// The slots each element of a selector carries the theme's classes for, and whether the sink always shows one.
const SLOTS: readonly (readonly [selector: string, slot: string, seen: boolean])[] = [
  ['form.jfb-form', 'form', true],
  ['.jfb-page', 'page', true],
  ['.jfb-group', 'group', true],
  ['.jfb-field', 'field', true],
  ['label.jfb-label, legend.jfb-label', 'label', true],
  ['.jfb-part > label', 'label', true],
  ['.jfb-help', 'help', true],
  ['.jfb-error', 'error', false],
  ['input.jfb-control, textarea.jfb-control', 'control', true],
  ['input[type=file]', 'control', true],
  ['select.jfb-control', 'select', true],
  ['.jfb-choices', 'choices', true],
  ['.jfb-choice', 'choice', true],
  ['.jfb-choice > input:is([type=radio], [type=checkbox])', 'choiceInput', true],
  ['.jfb-choice > label', 'choiceLabel', true],
  ['input[type=range]', 'range', true],
  ['.jfb-affix', 'affix', false],
  ['.jfb-affix > span', 'affixText', false],
  ['.jfb-counter', 'counter', false],
  ['.jfb-navigation', 'navigation', true],
  ['.jfb-button-primary', 'buttonPrimary', true],
  ['.jfb-button-link', 'buttonLink', false],
  ['.jfb-button:not(.jfb-button-primary):not(.jfb-button-link)', 'button', true],
  ['.jfb-progress', 'progress', false],
  ['.jfb-error-summary', 'errorSummary', false],
];

/** A field of the model with where it stands: its name's path, its control's id and whether a rule hides it. */
interface Placed {
  readonly field: FieldViewModel;
  readonly path: string | null;
  readonly id: string | null;
  readonly hidden: boolean;
}

/**
 * Every field of the form, a repeatable group's rows' fields among them: a row's field's alias is its row's path and
 * its own alias, and its id the instance's, the group's alias, the row's index and its own alias (ADR-0015).
 */
function fieldsOf(model: FormViewModel): Placed[] {
  const placed: Placed[] = [];
  const visit = (field: FieldViewModel, ids: string, hidden: boolean): void => {
    const path = UNNAMED.has(field.type) ? null : (field.alias ?? null);
    const own = path === null ? null : path.slice(path.lastIndexOf('.') + 1);
    placed.push({ field, path, id: own === null ? null : ids + own, hidden: hidden || field.isHidden });
    if (field.control?.kind === 'repeater') {
      for (const row of field.control.rows) {
        for (const child of row.fields) visit(child, `${ids}${own}-${row.index}-`, hidden || field.isHidden);
      }
    }
  };
  for (const page of model.pages) {
    for (const group of page.groups) {
      for (const field of group.fields) visit(field, `${model.id}-`, page.isHidden || group.isHidden);
    }
  }
  return placed;
}

/** Whether a control's name is the field's: its path, a part or a row after it, or a consent's, rows' or order's. */
function named(name: string, path: string, names: ElementCase['names']): boolean {
  const prefix = [names.consentPrefix, names.rowsPrefix, names.orderPrefix].find((each) => name.startsWith(each));
  const own = prefix === undefined ? name : name.slice(prefix.length);
  return name === names.nav || own === path || own.startsWith(`${path}.`) || own.startsWith(`${path}[`);
}

const ids = (element: Element, attribute: string): string[] =>
  (element.getAttribute(attribute) ?? '').split(' ').filter((id) => id.length > 0);

const sinks = (cases: readonly ElementCase[]) =>
  cases.filter((each) => each.name.startsWith('sink')).map((each) => [each.name, each] as const);

describe.each(LANGUAGES)('the contract on the element in %s', (_language, cases) => {
  it.each(sinks(cases))('%s: the form carries its hooks', (_name, element) => {
    const model = element.answer.form;
    const form = writeForm({ document, api: model.apiBase }, model);

    expect(form.localName).toBe('form');
    expect(form.className.split(' ')).toEqual(expect.arrayContaining(['jfb-form', `jfb-theme-${element.theme}`]));
    expect([
      form.id,
      form.dataset['jfbForm'],
      form.dataset['jfbRevision'],
      form.dataset['jfbApi'],
      form.dataset['jfbContract'],
    ]).toEqual([model.id, model.key, String(model.revision), model.apiBase, '1']);
    expect([
      form.getAttribute('lang'),
      form.getAttribute('dir'),
      form.getAttribute('method'),
      form.hasAttribute('novalidate'),
    ]).toEqual([element.culture, 'ltr', 'post', true]);
    expect(form.querySelectorAll(':scope > script[type="application/json"][data-jfb-model]')).toHaveLength(1);
    expect(form.querySelectorAll(':scope > template[data-jfb-templates]')).toHaveLength(1);
    expect(form.querySelectorAll(':scope > [data-jfb-live][aria-live="polite"]')).toHaveLength(1);
    const honeypot = form.querySelector(':scope > .jfb-hp');
    expect(honeypot?.getAttribute('aria-hidden')).toBe('true');
    expect(honeypot?.querySelector(`label[for="${honeypot.querySelector('input')?.id}"]`)).not.toBeNull();
    for (const button of form.querySelectorAll('.jfb-navigation > button')) {
      expect([button.getAttribute('type'), button.getAttribute('name'), button.getAttribute('data-jfb-nav')]).toEqual([
        'submit',
        element.names.nav,
        button.getAttribute('value'),
      ]);
      expect(['back', 'next', 'submit']).toContain(button.getAttribute('value'));
    }
  });

  it.each(sinks(cases))("%s: each field carries the contract's hooks", (name, element) => {
    const model = element.answer.form;
    const form = writeForm({ document, api: model.apiBase }, model);
    const fields = fieldsOf(model);

    expect(fields.length).toBeGreaterThan(40);
    expect(
      fields.filter((placed) => !placed.hidden).length,
      'every field shown, or all but the rule trigger hidden',
    ).toBe(name === 'sink-hidden' ? 1 : fields.length);
    for (const { field, path, id, hidden } of fields) {
      const at = `${name}, ${field.type} ${path ?? field.key}`;
      const wrapper = form.querySelector(`[data-jfb-field="${field.key}"]`);
      expect(wrapper, at).not.toBeNull();
      if (wrapper === null) continue;
      expect(wrapper.className.split(' ').slice(0, 2), at).toEqual(['jfb-field', `jfb-field-${field.type}`]);
      expect(wrapper.getAttribute('data-jfb-type'), at).toBe(field.type);
      expect(wrapper.getAttribute('data-jfb-alias'), at).toBe(path);
      expect(wrapper.closest('[hidden][data-jfb-hidden]') !== null, `${at}: hidden by its rule or its group's`).toBe(
        hidden,
      );
      expect(
        [...wrapper.querySelectorAll('input, select, textarea, button')].map((control) =>
          control.hasAttribute('disabled'),
        ),
        `${at}: a hidden field's controls are disabled`,
      ).toEqual(expect.not.arrayContaining([!hidden]));
      if (path === null || id === null) continue;
      const control = wrapper.querySelector(`[id="${id}"]`);
      expect(control, `${at}: the control's id below the instance`).not.toBeNull();
      if (control === null) continue;
      const fieldset = wrapper.querySelector(':scope > fieldset');
      const described = fieldset ?? control;
      expect(
        fieldset?.querySelector(':scope > legend') ?? wrapper.querySelector(`label[for="${id}"]`),
        `${at}: a legend or a label`,
      ).not.toBeNull();
      expect(
        [...wrapper.querySelectorAll('[name]')]
          .map((each) => each.getAttribute('name') ?? '')
          .filter((each) => !named(each, path, element.names)),
        `${at}: named by the alias or the row's path`,
      ).toEqual([]);
      expect(wrapper.querySelector(`[id="${id}-help"]`)?.textContent ?? null, at).toBe(field.help ?? null);
      expect(ids(described, 'aria-describedby').includes(`${id}-help`), `${at}: the help described`).toBe(
        field.help !== undefined,
      );
      expect(described.getAttribute('aria-invalid'), `${at}: no error on a first render`).toBeNull();
    }
    expect(form.querySelectorAll('[data-jfb-error]')).toHaveLength(0);
    expect(form.querySelector('.jfb-error-summary')).toBeNull();
  });

  it.each(sinks(cases))("%s: each slot carries the theme's classes beside ours", (name, element) => {
    const form = writeForm({ document, api: element.answer.form.apiBase }, element.answer.form);
    const theme = (slot: string): string[] =>
      (element.themeClasses[slot] ?? '').split(' ').filter((each) => each.length > 0);
    const allowed = new Set(Object.values(element.themeClasses).flatMap((classes) => classes.split(' ')));

    for (const [selector, slot, seen] of SLOTS) {
      const elements = selector === 'form.jfb-form' ? [form] : [...form.querySelectorAll(selector)];
      if (seen) expect(elements.length, `${name}: the sink shows ${selector}`).toBeGreaterThan(0);
      for (const each of elements)
        expect([...each.classList], `${name}: ${selector} carries ${slot}`).toEqual(
          expect.arrayContaining(theme(slot)),
        );
    }
    const foreign = new Set(
      [...form.querySelectorAll('[class]'), form]
        .flatMap((each) => [...each.classList])
        .filter((each) => !each.startsWith('jfb-') && !allowed.has(each)),
    );
    expect([...foreign], `${name}: every class is ours or the theme's`).toEqual([]);
  });

  it.each(sinks(cases))('%s: the repeatable group writes its row with its ids and its path', (_name, element) => {
    const model = element.answer.form;
    const form = writeForm({ document, api: model.apiBase }, model);
    const repeater = fieldsOf(model).find((placed) => placed.field.type === 'repeater');
    expect(repeater, 'the sink has a repeatable group').toBeDefined();
    if (repeater === undefined) return;
    const row = form.querySelector(`[data-jfb-field="${repeater.field.key}"] .jfb-row`);

    expect(row?.id).toBe(`${model.id}-${repeater.field.alias}-0`);
    expect(row?.getAttribute('data-jfb-row')).toBe('0');
    const consent = fieldsOf(model).find((placed) => placed.field.type === 'consent' && placed.path?.includes('['));
    expect(
      [...(row?.querySelectorAll('input[type="hidden"]') ?? [])].map((input) => input.getAttribute('name')),
    ).toContain(element.names.consentPrefix + consent?.path);
  });
});
