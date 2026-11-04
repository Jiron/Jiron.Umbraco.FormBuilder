// Changing a field's type (ROADMAP 9.4d, ADR-0059, ADR-0174), on the changes the server serves for the built-in types:
// the preview names what the field has that goes and what it needs, a lock of the new type takes its setting out of
// both, and the field once changed keeps what the change keeps and nothing it loses.
import { describe, expect, it } from 'vitest';
import type {
  FieldSettingLockResponseModel,
  FieldSettingResponseModel,
  FieldTypeChangeResponseModel,
  FieldTypeResponseModel,
} from '../../api';
import type { DesignDefinition, DesignField } from '../definition';
import { builtInChanges, builtInTypes } from '../preview/fixtures';
import { changedField, conditionsOn, previewChange, referencesTo, takes } from './type-change';

const type = (alias: string): FieldTypeResponseModel => {
  const found = builtInTypes[alias];
  if (!found) throw new Error(`No built-in type ${alias}`);
  return found;
};

const changeOf = (from: string, to: string): FieldTypeChangeResponseModel => {
  const found = builtInChanges[from]?.changes.find((change) => change.to === to);
  if (!found) throw new Error(`No change from ${from} to ${to}`);
  return found;
};

const NAME: DesignField = {
  key: 'n',
  type: 'text',
  alias: 'name',
  label: { '': 'Your name' },
  help: { '': 'As on your passport' },
  placeholder: { '': 'Your full name' },
  required: true,
  settings: { maxLength: 50, counter: true },
  validation: [{ kind: 'pattern', pattern: '[A-Z].*' }],
  messages: { required: { '': 'Tell us your name.' }, minLength: { '': 'Longer, please.' } },
  rules: [{ key: 'r0', effect: 'show', conditions: [{ field: 'p', op: 'isNotEmpty' }] }],
};

const PHONE: DesignField = {
  key: 'p',
  type: 'text',
  alias: 'phone',
  label: { '': 'Phone' },
  rules: [
    {
      key: 'r1',
      effect: 'show',
      match: 'any',
      conditions: [
        { field: 'n', op: 'contains', value: 'a' },
        {
          match: 'all',
          conditions: [
            { field: 'n', op: 'isNotEmpty' },
            { field: 'x', op: 'equals', value: { field: 'n' } },
          ],
        },
      ],
    },
  ],
};

const form = (...fields: DesignField[]): DesignDefinition => ({
  pages: [{ key: 'page', groups: [{ key: 'g', fields }] }],
});

/** A type whose setting the host locks, with the lock's value where it shows. */
function locked(alias: string, name: string, lock: FieldSettingLockResponseModel): FieldTypeResponseModel {
  const base = type(alias);
  return {
    ...base,
    settings: base.settings.map((setting) => (setting.name === name ? { ...setting, lock } : setting)),
  };
}

/** A served change under a lock of its new type's setting, as the server serves it then. */
const underLock = (change: FieldTypeChangeResponseModel, name: string): FieldTypeChangeResponseModel => ({
  ...change,
  keptSettings: change.keptSettings.filter((one) => one !== name),
  narrowedSettings: change.narrowedSettings.filter((one) => one !== name),
  neededSettings: change.neededSettings.filter((one) => one !== name),
  lockedSettings: [...change.keptSettings, ...change.narrowedSettings].filter((one) => one === name),
});

describe('a change of type', () => {
  it('names what a text with a placeholder and a condition using contains loses as a dropdown, and what it needs', () => {
    const preview = previewChange(
      form(NAME, PHONE),
      NAME,
      type('text'),
      type('dropdown'),
      changeOf('text', 'dropdown'),
    );

    expect(preview.to).toBe('dropdown');
    expect(preview.settings.map(({ setting, value }) => [setting.name, value])).toEqual([
      ['maxLength', 50],
      ['counter', true],
    ]);
    expect(preview.properties).toEqual(['placeholder', 'patternCheck']);
    expect(preview.messages).toEqual(['minLength']);
    expect(preview.conditions).toEqual([{ holder: { kind: 'element', key: 'p' }, op: 'contains' }]);
    expect(preview.needsOptions).toBe(true);
    expect(preview.needs).toEqual([]);
    expect(preview.locked).toEqual([]);
    expect(preview.newColumns).toBe(false);
  });

  it('changes the field: its type, without what goes, and with everything else as it was', () => {
    const changed = changedField(NAME, type('dropdown'), changeOf('text', 'dropdown'));

    expect(changed).toEqual({
      key: 'n',
      type: 'dropdown',
      alias: 'name',
      label: { '': 'Your name' },
      help: { '': 'As on your passport' },
      required: true,
      messages: { required: { '': 'Tell us your name.' } },
      rules: NAME.rules,
    });
    expect(NAME.type).toBe('text');
  });

  it('keeps the settings both types take, and the conditions that name the field, which publishing checks', () => {
    const boxes: DesignField = {
      key: 'b',
      type: 'checkboxes',
      alias: 'topics',
      settings: { minSelections: 2, maxSelections: 5, other: true, layout: 'cards' },
      options: { items: [{ value: 'a', places: 3 }, { value: 'b' }] },
    };
    const definition = form(boxes, {
      ...PHONE,
      rules: [{ key: 'r', effect: 'show', conditions: [{ field: 'b', op: 'isChecked' }] }],
    });

    const dropdown = previewChange(
      definition,
      boxes,
      type('checkboxes'),
      type('dropdown'),
      changeOf('checkboxes', 'dropdown'),
    );
    expect(dropdown.settings.map(({ setting }) => setting.name)).toEqual(['other', 'layout']);
    expect(dropdown.conditions).toEqual([]);
    expect(changedField(boxes, type('dropdown'), changeOf('checkboxes', 'dropdown'))).toEqual({
      ...boxes,
      type: 'dropdown',
      settings: { minSelections: 2, maxSelections: 5 },
    });

    const text = previewChange(definition, boxes, type('checkboxes'), type('text'), changeOf('checkboxes', 'text'));
    expect(text.properties).toEqual(['options', 'places']);
    expect(text.conditions).toEqual([{ holder: { kind: 'element', key: 'p' }, op: 'isChecked' }]);
    const asText = changedField(boxes, type('text'), changeOf('checkboxes', 'text'));
    expect([asText.type, asText['options'], asText['settings']]).toEqual(['text', undefined, undefined]);
  });

  it('takes the places off options that stay, where the new type has none', () => {
    const boxes: DesignField = {
      key: 'b',
      type: 'checkboxes',
      options: { source: 'items', items: [{ value: 'a', places: 3 }, { value: 'b' }] },
    };
    const change = changeOf('checkboxes', 'ranking');

    expect(previewChange(form(boxes), boxes, type('checkboxes'), type('ranking'), change).properties).toEqual([
      'places',
    ]);
    expect(changedField(boxes, type('ranking'), change)['options']).toEqual({
      source: 'items',
      items: [{ value: 'a' }, { value: 'b' }],
    });
  });

  it('carries a narrowed setting only when the new type takes its value', () => {
    const long = (maxLength: number): DesignField => ({
      key: 'l',
      type: 'textarea',
      settings: { maxLength, minWords: 3 },
    });
    const change = changeOf('textarea', 'text');

    const tooLong = previewChange(form(long(10_000)), long(10_000), type('textarea'), type('text'), change);
    expect(tooLong.settings.map(({ setting, value }) => [setting.name, value])).toEqual([
      ['maxLength', 10_000],
      ['minWords', 3],
    ]);
    expect(changedField(long(10_000), type('text'), change)['settings']).toBeUndefined();
    expect(
      previewChange(form(long(300)), long(300), type('textarea'), type('text'), change).settings.map(
        ({ setting }) => setting.name,
      ),
    ).toEqual(['minWords']);
    expect(changedField(long(300), type('text'), change)['settings']).toEqual({ maxLength: 300 });
  });

  it('with a lock of the new type, neither carries its setting over nor asks for it, and says where the value goes', () => {
    const boxes: DesignField = { key: 'b', type: 'checkboxes', settings: { minSelections: 2, maxSelections: 5 } };
    const dropdown = locked('dropdown', 'maxSelections', {
      hidden: false,
      configurationKey: 'JironFormBuilder:Designer:SettingLocks:2',
      value: 3,
    });
    const change = underLock(changeOf('checkboxes', 'dropdown'), 'maxSelections');

    const preview = previewChange(form(boxes), boxes, type('checkboxes'), dropdown, change);
    expect(preview.locked.map(({ setting, value }) => [setting.name, setting.lock?.value, value])).toEqual([
      ['maxSelections', 3, 5],
    ]);
    expect([preview.settings, preview.needs]).toEqual([[], []]);
    expect(changedField(boxes, dropdown, change)['settings']).toEqual({ minSelections: 2 });

    // A field without a value of its own for the locked setting loses nothing to the lock.
    const without: DesignField = { ...boxes, settings: { minSelections: 2 } };
    expect(previewChange(form(without), without, type('checkboxes'), dropdown, change).locked).toEqual([]);

    const same: DesignField = { ...boxes, settings: { maxSelections: 3 } };
    expect(previewChange(form(same), same, type('checkboxes'), dropdown, change).locked).toEqual([]);
    const hidden = locked('dropdown', 'maxSelections', { hidden: true, configurationKey: 'k', value: null });
    expect(
      previewChange(form(same), same, type('checkboxes'), hidden, change).locked.map(({ value }) => value),
    ).toEqual([3]);
  });

  it('asks for a required setting the field has no value for, unless a lock sets it', () => {
    const plain: DesignField = { key: 'c', type: 'checkbox', defaultValue: 'true' };
    const worded: DesignField = { ...plain, settings: { text: { '': 'I agree' } } };
    const change = changeOf('checkbox', 'consent');

    expect(
      previewChange(form(plain), plain, type('checkbox'), type('consent'), change).needs.map(({ name }) => name),
    ).toEqual(['text']);
    expect(previewChange(form(worded), worded, type('checkbox'), type('consent'), change).needs).toEqual([]);
    expect(previewChange(form(worded), worded, type('checkbox'), type('consent'), change).properties).toEqual([
      'defaultValue',
    ]);
    const lockedText = locked('consent', 'text', { hidden: true, configurationKey: 'k', value: null });
    expect(previewChange(form(plain), plain, type('checkbox'), lockedText, underLock(change, 'text')).needs).toEqual(
      [],
    );
    expect(
      previewChange(form(NAME), NAME, type('text'), type('calculation'), changeOf('text', 'calculation')).needs.map(
        ({ name }) => name,
      ),
    ).toEqual(['formula']);
  });

  it('takes away being required with the rules that require it, and keeps the rules that show the field', () => {
    const rules = [
      { key: 'show', effect: 'show', conditions: [] },
      { key: 'require', effect: 'require', conditions: [] },
    ];
    const field: DesignField = { ...NAME, rules };
    const change = changeOf('text', 'calculation');

    expect(previewChange(form(field), field, type('text'), type('calculation'), change).properties).toEqual([
      'placeholder',
      'required',
      'patternCheck',
    ]);
    const changed = changedField(field, type('calculation'), change);
    expect([changed['required'], changed.rules, changed['messages'], changed['validation']]).toEqual([
      undefined,
      [rules[0]],
      undefined,
      undefined,
    ]);
    const onlyRule: DesignField = { key: 'o', type: 'text', rules: rules.slice(1) };
    expect(changedField(onlyRule, type('calculation'), change).rules).toBeUndefined();
  });

  it('loses nothing where the field uses nothing that goes', () => {
    const bare: DesignField = { key: 't', type: 'text', label: { '': 'Comment' } };
    const preview = previewChange(form(bare), bare, type('text'), type('textarea'), changeOf('text', 'textarea'));
    expect([
      preview.settings,
      preview.locked,
      preview.properties,
      preview.messages,
      preview.conditions,
      preview.references,
      preview.needs,
      preview.needsOptions,
    ]).toEqual([[], [], [], [], [], [], [], false]);
    expect(changedField(bare, type('textarea'), changeOf('text', 'textarea'))).toEqual({ ...bare, type: 'textarea' });
  });

  it('says the entries screen reads answers elsewhere where the columns differ', () => {
    const amount: DesignField = { key: 'a', type: 'number' };
    expect(
      previewChange(form(amount), amount, type('number'), type('text'), changeOf('number', 'text')).newColumns,
    ).toBe(true);
  });
});

describe('the other fields that name a field', () => {
  it('are those whose options follow its answer, where the new type has no options, and those that must match it', () => {
    const size: DesignField = { key: 'size', type: 'dropdown', options: { items: [{ value: 's' }] } };
    const colour: DesignField = {
      key: 'colour',
      type: 'radio',
      options: { parent: 'size', items: [{ value: 'red' }] },
    };
    const mail: DesignField = { key: 'mail', type: 'email' };
    const again: DesignField = { key: 'again', type: 'email', validation: [{ kind: 'matchesField', field: 'mail' }] };
    const definition = form(size, colour, mail, again);

    expect(referencesTo(definition, 'size', type('text'), changeOf('checkboxes', 'text'))).toEqual([
      { kind: 'parent', key: 'colour' },
    ]);
    expect(referencesTo(definition, 'size', type('dropdown'), changeOf('checkboxes', 'dropdown'))).toEqual([]);
    expect(referencesTo(definition, 'mail', type('name'), changeOf('text', 'name'))).toEqual([
      { kind: 'matches', key: 'again' },
    ]);
    expect(referencesTo(definition, 'mail', type('hidden'), changeOf('text', 'hidden'))).toEqual([
      { kind: 'matches', key: 'again' },
    ]);
    expect(referencesTo(definition, 'mail', type('textarea'), changeOf('text', 'textarea'))).toEqual([]);
    expect(
      previewChange(definition, size, type('checkboxes'), type('text'), changeOf('checkboxes', 'text')).references,
    ).toEqual([{ kind: 'parent', key: 'colour' }]);
  });
});

describe('the conditions on a field', () => {
  it('are found in every rule and named by what holds them; an operand naming it is none', () => {
    const definition: DesignDefinition = {
      pages: [
        {
          key: 'page',
          rules: [{ key: 'pr', effect: 'show', conditions: [{ field: 'n', op: 'equals', value: 'x' }] }],
          groups: [
            {
              key: 'g',
              rules: [{ key: 'gr', effect: 'hide', conditions: [{ field: 'n', op: 'isEmpty' }] }],
              fields: [NAME, PHONE, { key: 'kids', type: 'repeater', fields: [{ ...PHONE, key: 'child' }] }],
            },
          ],
        },
      ],
      validationRules: [{ key: 'vr', field: 'p', conditions: [{ field: 'n', op: 'startsWith', value: 'a' }] }],
      actions: [
        {
          key: 'a1',
          type: 'email',
          name: 'Thank you mail',
          rules: [{ conditions: [{ field: 'n', op: 'matches', value: 'x' }] }],
        },
        { key: 'a2', type: 'webhook', rules: [{ conditions: [{ field: 'n', op: 'endsWith', value: 'x' }] }] },
      ],
      outcomes: [
        { key: 'o1' },
        { key: 'o2', rules: [{ conditions: [{ field: 'n', op: 'notContains', value: 'x' }] }] },
      ],
    };

    expect(conditionsOn(definition, 'n')).toEqual([
      { holder: { kind: 'element', key: 'page' }, op: 'equals' },
      { holder: { kind: 'element', key: 'g' }, op: 'isEmpty' },
      { holder: { kind: 'element', key: 'p' }, op: 'contains' },
      { holder: { kind: 'element', key: 'p' }, op: 'isNotEmpty' },
      { holder: { kind: 'element', key: 'child' }, op: 'contains' },
      { holder: { kind: 'element', key: 'child' }, op: 'isNotEmpty' },
      { holder: { kind: 'validationRule', key: 'vr', field: 'p' }, op: 'startsWith' },
      { holder: { kind: 'action', key: 'a1', name: 'Thank you mail', index: 0 }, op: 'matches' },
      { holder: { kind: 'action', key: 'a2', name: null, index: 1 }, op: 'endsWith' },
      { holder: { kind: 'outcome', key: 'o2', index: 1 }, op: 'notContains' },
    ]);
    // The field the answer is compared with is the condition's own; the answer it is compared to is none of it.
    expect(conditionsOn(definition, 'x').map(({ holder, op }) => [holder.key, op])).toEqual([
      ['p', 'equals'],
      ['child', 'equals'],
    ]);
    expect(conditionsOn(definition, 'none')).toEqual([]);
  });
});

describe('whether a setting takes a value', () => {
  const setting = (
    kind: string,
    rules: Partial<FieldSettingResponseModel['rules']> = {},
    extra: Partial<FieldSettingResponseModel> = {},
  ): FieldSettingResponseModel => ({
    name: 's',
    kind,
    choices: [],
    settings: [],
    label: 's',
    editor: kind,
    group: 'essentials',
    supportsMergeTags: false,
    isPublic: true,
    rules: { required: false, minimumIsExclusive: false, maximumIsExclusive: false, allowedValues: [], ...rules },
    ...extra,
  });

  it('holds each kind to its rules, as the server does', () => {
    expect(takes(setting('wholeNumber', { minimum: '1', maximum: '10' }), 10)).toBe(true);
    expect(takes(setting('wholeNumber', { minimum: '1', maximum: '10' }), 11)).toBe(false);
    expect(takes(setting('wholeNumber'), 1.5)).toBe(false);
    expect(takes(setting('wholeNumber'), '3')).toBe(false);
    expect(takes(setting('number', { maximum: '2.5' }), '2.5')).toBe(true);
    expect(takes(setting('number', { maximum: '2.5', maximumIsExclusive: true }), '2.5')).toBe(false);
    expect(takes(setting('text', { maximumLength: 3 }), 'abcd')).toBe(false);
    expect(takes(setting('text', { allowedValues: ['a'] }), 'a')).toBe(true);
    expect(takes(setting('boolean'), 'true')).toBe(false);
    expect(takes(setting('choice', {}, { choices: [{ value: 'a', label: 'A' }] }), 'b')).toBe(false);
    expect(takes(setting('choice', {}, { choices: [{ value: 'a', label: 'A' }] }), 'a')).toBe(true);
    expect(takes(setting('localizedText', { maximumLength: 3 }), { '': 'abc', de: 'abcd' })).toBe(false);
    expect(takes(setting('localizedHtml', { required: true }), { '': ' ' })).toBe(false);
    expect(takes(setting('localizedHtml', { required: true }), { '': 'I agree' })).toBe(true);
    expect(takes(setting('list', { maximumCount: 2 }), ['a', 'b', 'c'])).toBe(false);
    expect(takes(setting('list', { maximumCount: 2 }), ['a', 'b'])).toBe(true);
    expect(takes(setting('json'), { any: 1 })).toBe(false);
    expect(takes(setting('text', { required: true }), undefined)).toBe(false);
    expect(takes(setting('text'), null)).toBe(false);
    expect(takes(setting('text', { minimumLength: 2 }), '')).toBe(false);
    expect(takes(setting('text', { required: true }), '  ')).toBe(false);
    expect(takes(setting('number'), '2.50')).toBe(true);
    expect(takes(setting('number'), '.5')).toBe(false);
    expect(takes(setting('number'), '1234567890123456')).toBe(false);
    expect(takes(setting('date'), '2026-12-31')).toBe(true);
    expect(takes(setting('date'), '2026-02-30')).toBe(false);
    expect(takes(setting('time'), '09:30')).toBe(true);
    expect(takes(setting('time'), '24:00')).toBe(false);
    expect(takes(setting('key'), '5c0f9b5e-3a4d-4b8e-9b51-6f1d2c3a4b5c')).toBe(true);
    expect(takes(setting('key'), '5C0F9B5E-3A4D-4B8E-9B51-6F1D2C3A4B5C')).toBe(false);
    expect(takes(setting('choice'), 'a')).toBe(false);
    expect(takes(setting('localizedText', { minimumLength: 2 }), { '': 'ab', de: '' })).toBe(false);
    expect(takes(setting('localizedText'), { de: 'Hallo' })).toBe(false);
    expect(takes(setting('localizedText'), { '': 'Hi', 'de-DE': 'Hallo', 'DE-de': 'Servus' })).toBe(false);
    expect(takes(setting('localizedText'), { '': 'Hi', 'not a culture': 'x' })).toBe(false);
    expect(takes(setting('localizedText', { required: true }), { '': ' ', de: 'Hallo' })).toBe(false);
    expect(takes(setting('localizedText', { allowedValues: ['Hi'] }), { '': 'Hello' })).toBe(false);
    expect(takes(setting('list', {}, { itemKind: 'number' }), ['1.5', '1.50'])).toBe(false);
    expect(takes(setting('list', {}, { itemKind: 'date' }), ['2026-12-24', '2026-12-25'])).toBe(true);
    expect(takes(setting('list', { minimumCount: 2 }, { itemKind: 'text' }), ['a'])).toBe(false);
    const nested = setting(
      'nested',
      {},
      {
        settings: [
          setting('wholeNumber', { maximum: '5' }, { name: 'n' }),
          setting('text', { required: true }, { name: 't' }),
        ],
      },
    );
    expect(takes(nested, { n: 3, t: 'x' })).toBe(true);
    expect(takes(nested, { n: 6, t: 'x' })).toBe(false);
    expect(takes(nested, { n: 3 })).toBe(false);
    expect(takes(nested, { n: 3, t: 'x', other: 1 })).toBe(false);
  });
});
