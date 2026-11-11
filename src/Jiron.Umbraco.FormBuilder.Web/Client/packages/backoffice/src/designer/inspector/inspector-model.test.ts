// What the inspector shows for each built-in type (ROADMAP 9.4a, IA 5.1, ADR-0170), from the field types endpoint's
// snapshot (docs/api/field-types.json): sections in their order, the common properties a type's features allow, its
// settings by their group, and none an editor of a later step draws.
import { describe, expect, it } from 'vitest';
import type { FieldTypeResponseModel } from '../../api';
import type { DesignField } from '../definition';
import { builtInTypes } from '../preview/fixtures';
import {
  fieldSections,
  groupSections,
  MOST_SHOWN,
  pageSections,
  PLAIN_EDITORS,
  splitItems,
  takesSeveral,
  untranslated,
  type Item,
  type Section,
} from './inspector-model';

const typeOf = (alias: string): FieldTypeResponseModel => {
  const found = builtInTypes[alias];
  if (!found) throw new Error(`no type ${alias}`);
  return found;
};

/** An item as a short word: a property's name, a message's kind, a setting's name. */
const nameOf = (item: Item): string => {
  switch (item.kind) {
    case 'text':
    case 'switch':
    case 'plain':
      return item.property;
    case 'message':
      return `message:${item.message}`;
    case 'setting':
      return `setting:${item.setting.name}`;
    case 'span':
      return `span:${item.columns}`;
    default:
      return item.kind;
  }
};

const shape = (sections: readonly Section[]) =>
  Object.fromEntries(sections.map((section) => [section.id, section.items.map(nameOf)]));

describe("a field's sections", () => {
  it('are those of a text field: its common properties, its settings by group, its checks and messages', () => {
    expect(shape(fieldSections(typeOf('text'), 1))).toEqual({
      essentials: ['label', 'help', 'required', 'placeholder'],
      validation: [
        'setting:minLength',
        'setting:maxLength',
        'pattern',
        'unique',
        'matches',
        'message:required',
        'message:minLength',
        'message:maxLength',
      ],
      rules: [],
      appearance: ['hideLabel', 'setting:prefix', 'setting:suffix', 'setting:counter'],
      advanced: [
        'alias',
        'defaultValue',
        'prefill',
        'autocomplete',
        'sensitive',
        'keep',
        'includeInEmails',
        'cssClass',
      ],
    });
  });

  it("are a number field's and a date field's, with a width in a group of columns", () => {
    const number = shape(fieldSections(typeOf('number'), 3));
    expect(number['validation']).toEqual([
      'setting:min',
      'setting:max',
      'setting:step',
      'setting:decimals',
      'unique',
      'matches',
      'message:required',
      'message:invalid',
      'message:min',
      'message:max',
      'message:step',
      'message:digits',
    ]);
    expect(number['appearance']).toEqual(['hideLabel', 'span:3', 'setting:prefix', 'setting:suffix']);
    const date = shape(fieldSections(typeOf('date'), 1));
    expect(date['essentials']).toEqual(['label', 'help', 'required', 'setting:defaultToday']);
    expect(date['validation']).toEqual([
      'setting:min',
      'setting:max',
      'setting:weekdaysOnly',
      'setting:excluded',
      'unique',
      'matches',
      'message:required',
      'message:invalid',
      'message:dateWindow',
    ]);
  });

  it('leave out a section with nothing in it, Rules where the type takes none, and a hidden lock', () => {
    const heading = fieldSections(typeOf('heading'), 1);
    expect(heading.map((section) => section.id)).toEqual(['essentials', 'rules', 'appearance', 'advanced']);
    expect(shape(heading)).toEqual({
      essentials: ['setting:text'],
      rules: [],
      appearance: ['setting:level'],
      advanced: ['cssClass'],
    });
    const plain: FieldTypeResponseModel = { ...typeOf('divider'), features: [] };
    expect(fieldSections(plain, 1).map((section) => section.id)).toEqual(['advanced']);
    // A type that can only be required still has Rules, for "Required when" (ADR-0175).
    const requiredOnly: FieldTypeResponseModel = { ...typeOf('divider'), features: ['required'] };
    expect(fieldSections(requiredOnly, 1).map((section) => section.id)).toContain('rules');
    const text = typeOf('text');
    const hidden: FieldTypeResponseModel = {
      ...text,
      settings: text.settings.map((setting) =>
        setting.name === 'prefix' ? { ...setting, lock: { hidden: true, configurationKey: 'K' } } : setting,
      ),
    };
    expect(shape(fieldSections(hidden, 1))['appearance']).toEqual(['hideLabel', 'setting:suffix', 'setting:counter']);
    const locked: FieldTypeResponseModel = {
      ...text,
      settings: text.settings.map((setting) =>
        setting.name === 'maxLength'
          ? { ...setting, lock: { hidden: false, configurationKey: 'K', value: 9 } }
          : setting,
      ),
    };
    expect(shape(fieldSections(locked, 1))['validation']).toContain('setting:maxLength');
  });

  it('give a choice field its options in Essentials, and a grid its rows before its columns (ADR-0172)', () => {
    for (const alias of ['dropdown', 'radio', 'checkboxes', 'ranking'])
      expect(shape(fieldSections(typeOf(alias), 1))['essentials'], alias).toContain('options');
    const grid = shape(fieldSections(typeOf('grid'), 1))['essentials'] ?? [];
    expect(grid.indexOf('rows')).toBeGreaterThanOrEqual(0);
    expect(grid.indexOf('rows')).toBeLessThan(grid.indexOf('options'));
    expect(shape(fieldSections(typeOf('text'), 1))['essentials']).not.toContain('options');
  });

  it('show only settings the inspector draws: plain ones, rich text and pickers (ADR-0173)', () => {
    for (const type of Object.values(builtInTypes))
      for (const section of fieldSections(type, 1))
        for (const item of section.items)
          if (item.kind === 'setting')
            expect(PLAIN_EDITORS.has(item.setting.editor), `${type.alias}.${item.setting.name}`).toBe(true);
    expect(shape(fieldSections(typeOf('paragraph'), 1))['essentials'] ?? []).toContain('setting:html');
    expect(shape(fieldSections(typeOf('image'), 1))['essentials'] ?? []).toContain('setting:media');
    // A consent's and a checkbox's text, which sit in a label, are one line of rich text (inlineHtml).
    expect(shape(fieldSections(typeOf('consent'), 1))['essentials'] ?? []).toContain('setting:text');
    expect(shape(fieldSections(typeOf('checkbox'), 1))['essentials'] ?? []).toContain('setting:text');
  });

  it("show a setting whose editor a site's manifest draws, and only then (ADR-0233)", () => {
    const text = typeOf('text');
    const own: FieldTypeResponseModel = {
      ...text,
      settings: [
        ...text.settings,
        ...text.settings
          .slice(0, 1)
          .map((setting) => ({ ...setting, name: 'colours', editor: 'site.swatches', group: 'appearance' })),
      ],
    };
    expect(shape(fieldSections(own, 1))['appearance']).not.toContain('setting:colours');
    expect(shape(fieldSections(own, 1, (editor) => editor === 'site.swatches'))['appearance']).toContain(
      'setting:colours',
    );
  });

  it("take a list of answers as a default where the field takes several: choices, files, a dropdown's Multiple", () => {
    const field = (type: string, settings?: Record<string, unknown>): DesignField => ({
      key: 'f',
      type,
      ...(settings ? { settings } : {}),
    });
    expect(takesSeveral(typeOf('checkboxes'), field('checkboxes'))).toBe(true);
    expect(takesSeveral(typeOf('file'), field('file'))).toBe(true);
    expect(takesSeveral(typeOf('dropdown'), field('dropdown'))).toBe(false);
    expect(takesSeveral(typeOf('dropdown'), field('dropdown', { multiple: true }))).toBe(true);
    expect(takesSeveral(typeOf('text'), field('text', { multiple: true }))).toBe(false);
    expect(takesSeveral(typeOf('radio'), field('radio'))).toBe(false);
  });

  it("of a group and a page are their title, a group's columns and style, and their rules", () => {
    expect(shape(groupSections())).toEqual({ essentials: ['title', 'columns', 'style'], rules: [] });
    expect(shape(pageSections())).toEqual({ essentials: ['title'], rules: [] });
  });
});

describe('a section with many items', () => {
  it('shows seven before More options, as IA 5.1 says', () => {
    expect(MOST_SHOWN).toBe(7);
    const items = Array.from({ length: MOST_SHOWN + 2 }, (_, at): Item => ({ kind: 'message', message: `m${at}` }));
    const split = splitItems(items);
    expect(split.shown).toHaveLength(MOST_SHOWN);
    expect(split.more.map(nameOf)).toEqual([`message:m${MOST_SHOWN}`, `message:m${MOST_SHOWN + 1}`]);
    expect(splitItems(items.slice(0, MOST_SHOWN))).toEqual({ shown: items.slice(0, MOST_SHOWN), more: [] });
    expect(splitItems(items.slice(0, MOST_SHOWN + 1)).more).toHaveLength(1);
  });
});

describe('a field without a text in a language', () => {
  const text = typeOf('text');
  it('is one with a written text that has no words of its own in that language', () => {
    const field: DesignField = { key: 'f', type: 'text', label: { '': 'Name', 'de-DE': 'Name' } };
    expect(untranslated(field, text, 'de-DE')).toBe(false);
    expect(untranslated(field, text, 'fr-FR')).toBe(true);
    expect(untranslated({ ...field, label: { '': 'Name', de: 'Name' } }, text, 'de-CH')).toBe(false);
    expect(untranslated({ key: 'e', type: 'text' }, text, 'de-DE')).toBe(false);
    expect(untranslated({ ...field, label: { '': ' ' } }, text, 'de-DE')).toBe(false);
    // A label naming a dictionary item is the dictionary's to translate (ADR-0228).
    const email = typeOf('email');
    expect(
      untranslated({ key: 'm', type: 'email', label: { '': '#Demo.Login.Email' }, required: true }, email, 'de-DE'),
    ).toBe(false);
  });

  it('counts its help, placeholder, messages and text settings, nested ones too', () => {
    const translated: DesignField = { key: 'f', type: 'text', label: { '': 'Name', 'de-DE': 'Name' } };
    expect(untranslated({ ...translated, help: { '': 'Help' } }, text, 'de-DE')).toBe(true);
    expect(untranslated({ ...translated, placeholder: { '': 'Ada' } }, text, 'de-DE')).toBe(true);
    expect(untranslated({ ...translated, messages: { required: { '': 'Please' } } }, text, 'de-DE')).toBe(true);
    expect(untranslated({ ...translated, settings: { prefix: { '': 'EUR' } } }, text, 'de-DE')).toBe(true);
    expect(untranslated({ ...translated, settings: { maxLength: 40 } }, text, 'de-DE')).toBe(false);
    const calculation = typeOf('calculation');
    const formatted: DesignField = { key: 'c', type: 'calculation', settings: { format: { prefix: { '': '$' } } } };
    expect(untranslated(formatted, calculation, 'de-DE')).toBe(true);
    expect(untranslated(formatted, undefined, 'de-DE')).toBe(false);
  });

  it("counts a field's own options, their descriptions and a grid's rows (ADR-0172)", () => {
    const radio = typeOf('radio');
    const label = { '': 'Colour', 'de-DE': 'Farbe' };
    const red = { value: 'red', label: { '': 'Red', 'de-DE': 'Rot' } };
    const field = (items: unknown[]): DesignField => ({ key: 'r', type: 'radio', label, options: { items } });
    expect(untranslated(field([red]), radio, 'de-DE')).toBe(false);
    expect(untranslated(field([red, { value: 'blue', label: { '': 'Blue' } }]), radio, 'de-DE')).toBe(true);
    expect(untranslated(field([{ ...red, description: { '': 'Warm' } }]), radio, 'de-DE')).toBe(true);
    expect(untranslated(field([{ value: 'x' }]), radio, 'de-DE')).toBe(false);
    const grid: DesignField = { key: 'g', type: 'grid', label, rows: [{ key: 'k', label: { '': 'The talks' } }] };
    expect(untranslated(grid, typeOf('grid'), 'de-DE')).toBe(true);
  });
});
