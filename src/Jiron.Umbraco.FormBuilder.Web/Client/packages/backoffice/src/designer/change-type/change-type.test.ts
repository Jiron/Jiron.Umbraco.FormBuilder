// The Change type dialog and its words (ROADMAP 9.4d, ADR-0174): a preview says each setting with the field's value,
// a text property with its text, a message by what it answers and a condition by what holds it; the dialog lists the
// types by palette group in the browser's own select, a refused one not to be chosen and why, shows the preview of the
// type chosen, and changes only once one is chosen.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { FieldTypeChangeResponseModel, FieldTypeResponseModel } from '../../api';
import en from '../../lang/en';
import { must, registerEnglish, settle, shadow, text } from '../../testing';
import type { DesignDefinition, DesignField } from '../definition';
import { builtInChanges, builtInTypes } from '../preview/fixtures';
import { changeWords, type WordsContext } from './change-type-words';
import { ChangeTypeModalElement } from './change-type.modal.element';
import type { ChangeTypeModalData, ChangeTypeModalValue } from './change-type.modal.token';
import { previewChange } from './type-change';

type Dictionary = Readonly<Record<string, Readonly<Record<string, unknown>>>>;

/** A term of the English dictionary by its whole key, as the backoffice's localization reads it. */
function term(key: string, ...args: unknown[]): string {
  const at = key.indexOf('_');
  const entry = (en as Dictionary)[key.slice(0, at)]?.[key.slice(at + 1)];
  if (entry === undefined) throw new Error(`No term ${key}`);
  return typeof entry === 'function' ? String((entry as (...values: unknown[]) => unknown)(...args)) : String(entry);
}

const NAMES: Readonly<Record<string, string>> = { n: 'Your name', p: 'Phone', g: 'Contact' };

const context: WordsContext = {
  term,
  say: (text) => (text?.startsWith('#') ? term(text.slice(1)) : (text ?? '')),
  culture: '',
  nameOf: (key) => NAMES[key] ?? key,
};

const type = (alias: string): FieldTypeResponseModel => must(builtInTypes[alias], alias);
const changeOf = (from: string, to: string): FieldTypeChangeResponseModel =>
  must(
    builtInChanges[from]?.changes.find((change) => change.to === to),
    `${from} to ${to}`,
  );

const NAME: DesignField = {
  key: 'n',
  type: 'text',
  label: { '': 'Your name' },
  placeholder: { '': 'Your full name' },
  settings: { maxLength: 50 },
  validation: [{ kind: 'pattern', pattern: '[A-Z].*' }],
  messages: { minLength: { '': 'Longer, please.' } },
};

const DEFINITION: DesignDefinition = {
  pages: [
    {
      key: 'page',
      groups: [
        {
          key: 'g',
          fields: [
            NAME,
            {
              key: 'p',
              type: 'text',
              rules: [{ key: 'r', effect: 'show', conditions: [{ field: 'n', op: 'contains', value: 'a' }] }],
            },
          ],
        },
      ],
    },
  ],
  validationRules: [{ key: 'v', field: 'p', conditions: [{ field: 'n', op: 'startsWith', value: 'x' }] }],
  actions: [{ key: 'a', type: 'email', rules: [{ conditions: [{ field: 'n', op: 'matches', value: 'x' }] }] }],
  outcomes: [{ key: 'o', rules: [{ conditions: [{ field: 'n', op: 'endsWith', value: 'x' }] }] }],
};

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
});

describe('the words of a change', () => {
  it('say what goes with its value, what the field needs, and every condition to change by what holds it', () => {
    const preview = previewChange(DEFINITION, NAME, type('text'), type('dropdown'), changeOf('text', 'dropdown'));
    const words = changeWords(context, NAME, preview, false);
    const maxLength = term('jfbFieldSettings_textMaxLength');

    expect(words.goes).toEqual([
      `${maxLength}: 50`,
      'Placeholder: Your full name',
      'Must match a pattern: [A-Z].*',
      'Message when the answer is too short',
    ]);
    expect(words.needs).toEqual([
      'At least one option',
      'A rule of Phone uses "contains"',
      'A validation rule of Phone uses "starts with"',
      'The rule of action 1 uses "matches a pattern"',
      'The rule of outcome 1 uses "ends with"',
    ]);
    expect([words.locked, words.entries]).toEqual([[], null]);
  });

  it('name a lock by its value where the site shows it, and the entries where the form has them and the columns differ', () => {
    const boxes: DesignField = { key: 'b', type: 'checkboxes', settings: { maxSelections: 5 } };
    const dropdown = (hidden: boolean): FieldTypeResponseModel => ({
      ...type('dropdown'),
      settings: type('dropdown').settings.map((setting) =>
        setting.name === 'maxSelections'
          ? { ...setting, lock: { hidden, configurationKey: 'k', value: hidden ? null : 3 } }
          : setting,
      ),
    });
    const served = changeOf('checkboxes', 'dropdown');
    const change = { ...served, keptSettings: ['minSelections'], lockedSettings: ['maxSelections'] };
    const most = term('jfbFieldSettings_dropdownMaxSelections');

    const shown = changeWords(
      context,
      boxes,
      previewChange(DEFINITION, boxes, type('checkboxes'), dropdown(false), change),
      true,
    );
    expect([shown.locked, shown.goes, shown.needs, shown.entries]).toEqual([[`${most}: 3`], [], [], null]);
    const hidden = changeWords(
      context,
      boxes,
      previewChange(DEFINITION, boxes, type('checkboxes'), dropdown(true), change),
      true,
    );
    expect(hidden.locked).toEqual([most]);

    const amount: DesignField = { key: 'm', type: 'number' };
    const columns = previewChange(DEFINITION, amount, type('number'), type('text'), changeOf('number', 'text'));
    expect(changeWords(context, amount, columns, true).entries).toBe(term('jfbDesigner_changeTypeEntries'));
    expect(changeWords(context, amount, columns, false).entries).toBeNull();
  });

  it('name another field that names it, and an action and a validation rule however they are given', () => {
    const size: DesignField = { key: 'n', type: 'checkboxes', options: { items: [{ value: 's' }] } };
    const definition: DesignDefinition = {
      pages: [
        { key: 'page', groups: [{ key: 'g', fields: [size, { key: 'p', type: 'radio', options: { parent: 'n' } }] }] },
      ],
      validationRules: [{ key: 'v', conditions: [{ field: 'n', op: 'isChecked' }] }],
      actions: [
        { key: 'a', type: 'email', name: 'Thanks', rules: [{ conditions: [{ field: 'n', op: 'isNotChecked' }] }] },
      ],
    };
    const preview = previewChange(definition, size, type('checkboxes'), type('text'), changeOf('checkboxes', 'text'));
    expect(changeWords(context, size, preview, false).needs).toEqual([
      'A validation rule uses "is ticked"',
      'The rule of the action Thanks uses "is not ticked"',
      'The options of Phone follow its answer',
    ]);
  });

  it('name a needed setting by its label', () => {
    const preview = previewChange(DEFINITION, NAME, type('text'), type('calculation'), changeOf('text', 'calculation'));
    expect(changeWords(context, NAME, preview, false).needs[0]).toBe(term('jfbFieldSettings_calculationFormula'));
  });
});

/** A modal context as Umbraco's holds the value. */
function modalContext() {
  const state = new UmbObjectState<Partial<ChangeTypeModalValue>>({});
  return {
    value: state.asObservable(),
    setValue: (next: ChangeTypeModalValue) => state.setValue(next),
    updateValue: (partial: Partial<ChangeTypeModalValue>) => state.update(partial),
    getValue: () => state.getValue(),
    submit: vi.fn(),
    reject: vi.fn(),
  };
}

const NONE = { goes: [], locked: [], needs: [], entries: null };

const DATA: ChangeTypeModalData = {
  name: 'Your name',
  type: 'Short text',
  targets: [
    { alias: 'textarea', name: 'Long text', group: 'Text', refusal: null, words: NONE },
    {
      alias: 'dropdown',
      name: 'Dropdown',
      group: 'Choices',
      refusal: null,
      words: {
        goes: ['Placeholder: Your full name'],
        locked: ['Most selections: 3'],
        needs: ['At least one option', 'A rule of Phone uses "contains"'],
        entries: 'Older answers note',
      },
    },
    {
      alias: 'captcha',
      name: 'Captcha',
      group: 'Protection',
      refusal: 'A form holds one Captcha at most.',
      words: NONE,
    },
  ],
};

async function dialogOf(data: ChangeTypeModalData = DATA) {
  const modal = modalContext();
  const element = new ChangeTypeModalElement();
  element.modalContext = modal as never;
  element.data = data;
  document.body.append(element);
  await settle(element);
  return { element, modal, root: shadow(element) };
}

const confirm = (root: ShadowRoot) =>
  must(root.querySelector<HTMLElement & { disabled: boolean }>('uui-button[look="primary"]'), 'Change type');

async function choose(element: ChangeTypeModalElement, root: ShadowRoot, alias: string) {
  const select = must(root.querySelector('select'), 'the list');
  select.value = alias;
  select.dispatchEvent(new Event('change'));
  await settle(element);
}

describe('the Change type dialog', () => {
  it("lists the types by group in the browser's own select, a refused one not to be chosen and why", async () => {
    const { root } = await dialogOf();
    const select = must(root.querySelector('select'), 'the list');
    expect(text(root.querySelector('label[for="type"]'))).toBe('New type');
    expect([...select.querySelectorAll('optgroup')].map((group) => group.label)).toEqual([
      'Text',
      'Choices',
      'Protection',
    ]);
    expect([...select.options].map((option) => [option.value, text(option), option.disabled])).toEqual([
      ['', 'Choose a type', false],
      ['textarea', 'Long text', false],
      ['dropdown', 'Dropdown', false],
      ['captcha', 'Captcha (unavailable)', false],
    ]);
    expect(text(root.querySelector('#now'))).toBe('Now: Short text');
    expect(select.getAttribute('aria-describedby')).toBe('now');
    expect(text(root.querySelector('.preview'))).toBe(term('jfbDesigner_changeTypeChoose'));
    expect(confirm(root).disabled).toBe(true);
  });

  it('shows what the type chosen takes and needs, and changes to it', async () => {
    const { element, modal, root } = await dialogOf();
    await choose(element, root, 'dropdown');
    const preview = must(root.querySelector('.preview'), 'the preview');
    expect(preview.getAttribute('aria-live')).toBe('polite');
    expect([...preview.querySelectorAll('h3')].map((heading) => text(heading))).toEqual([
      'What goes',
      'Set by the site instead',
      'Needed before it publishes',
    ]);
    expect([...preview.querySelectorAll('li')].map((item) => text(item))).toEqual([
      'Placeholder: Your full name',
      'Most selections: 3',
      'At least one option',
      'A rule of Phone uses "contains"',
    ]);
    expect(text(preview.querySelector('.help'))).toBe('Older answers note');
    expect(confirm(root).disabled).toBe(false);
    confirm(root).dispatchEvent(new Event('click'));
    expect(modal.getValue()).toEqual({ to: 'dropdown' });
    expect(modal.submit).toHaveBeenCalledOnce();
  });

  it('says a change that takes nothing keeps everything, and Enter on a chosen type changes to it', async () => {
    const { element, modal, root } = await dialogOf();
    await choose(element, root, 'textarea');
    expect(text(root.querySelector('.preview'))).toBe(term('jfbDesigner_changeTypeKeepsAll'));
    const select = must(root.querySelector('select'), 'the list');
    select.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    expect(modal.getValue()).toEqual({ to: 'textarea' });
    expect(modal.submit).toHaveBeenCalledOnce();
  });

  it('gives the list the focus as it opens', async () => {
    const { root } = await dialogOf();
    await new Promise<void>((done) => requestAnimationFrame(() => done()));
    expect(root.activeElement).toBe(root.querySelector('select'));
  });

  it("says why a refused type can't be chosen, and changes to nothing while none, or a refused one, is chosen", async () => {
    const { element, modal, root } = await dialogOf();
    const select = must(root.querySelector('select'), 'the list');
    select.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    await choose(element, root, 'captcha');
    expect(text(root.querySelector('.preview .why'))).toBe('A form holds one Captcha at most.');
    expect(root.querySelectorAll('.preview li')).toHaveLength(0);
    select.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    confirm(root).dispatchEvent(new Event('click'));
    expect(modal.submit).not.toHaveBeenCalled();
    expect(confirm(root).disabled).toBe(true);
  });
});
